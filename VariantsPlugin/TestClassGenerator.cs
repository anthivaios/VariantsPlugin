using System.CodeDom;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Reqnroll;
using Reqnroll.Configuration;
using Reqnroll.Generator;
using Reqnroll.Generator.CodeDom;
using Reqnroll.Generator.Generation;
using Reqnroll.Generator.UnitTestConverter;
using Reqnroll.Generator.UnitTestProvider;
using Reqnroll.Parser;

namespace VariantsPlugin
{
    [SuppressMessage("ReSharper", "BitwiseOperatorOnEnumWithoutFlags")]
    public class TestClassGenerator
    {
        protected TestClassGenerationContext GenerationContext { get; private set; }

        private readonly IDecoratorRegistry _decoratorRegistry;
        private readonly IUnitTestGeneratorProvider _testGeneratorProvider;
        private readonly CodeDomHelper _codeDomHelper;
        private readonly ReqnrollConfiguration _specFlowConfiguration;
        private readonly ScenarioPartHelper _scenarioPartHelper;

        public TestClassGenerator(IDecoratorRegistry decoratorRegistry,
            IUnitTestGeneratorProvider testGeneratorProvider, CodeDomHelper codeDomHelper,
            ReqnrollConfiguration specFlowConfiguration)
        {
            _decoratorRegistry = decoratorRegistry;
            _testGeneratorProvider = testGeneratorProvider;
            _codeDomHelper = codeDomHelper;
            _specFlowConfiguration = specFlowConfiguration;
            _scenarioPartHelper = new ScenarioPartHelper(_specFlowConfiguration, _codeDomHelper);
        }

        public CodeNamespace CreateNamespace(string targetNamespace)
        {
            targetNamespace = targetNamespace ?? GeneratorConstants.DEFAULT_NAMESPACE;

            if (!targetNamespace.StartsWith("global", StringComparison.CurrentCultureIgnoreCase))
            {
                switch (_codeDomHelper.TargetLanguage)
                {
                    case CodeDomProviderLanguage.VB:
                        targetNamespace = $"GlobalVBNetNamespace.{targetNamespace}";
                        break;
                }
            }

            var codeNamespace = new CodeNamespace(targetNamespace);

            codeNamespace.Imports.Add(new CodeNamespaceImport(GeneratorConstants.REQNROLL_NAMESPACE));
            codeNamespace.Imports.Add(new CodeNamespaceImport("System"));
            codeNamespace.Imports.Add(new CodeNamespaceImport("System.Linq"));
            return codeNamespace;
        }

        public TestClassGenerationContext CreateTestClassStructure(CodeNamespace codeNamespace, string testClassName,
            ReqnrollDocument document)
        {
            var testClass = _codeDomHelper.CreateGeneratedTypeDeclaration(testClassName);
            codeNamespace.Types.Add(testClass);
            return new TestClassGenerationContext(
                _testGeneratorProvider,
                document,
                codeNamespace,
                testClass,
                DeclareTestRunnerMember(testClass),
                _codeDomHelper.CreateMethod(testClass),
                _codeDomHelper.CreateMethod(testClass),
                _codeDomHelper.CreateMethod(testClass),
                _codeDomHelper.CreateMethod(testClass),
                _codeDomHelper.CreateMethod(testClass),
                _codeDomHelper.CreateMethod(testClass),
                _codeDomHelper.CreateMethod(testClass),
                document.ReqnrollFeature.HasFeatureBackground() ? _codeDomHelper.CreateMethod(testClass) : null,
                _testGeneratorProvider.GetTraits().HasFlag(UnitTestGeneratorTraits.RowTests) &&
                _specFlowConfiguration.AllowRowTests);
        }

        private CodeMemberField DeclareTestRunnerMember(CodeTypeDeclaration type)
        {
            var testRunnerField = new CodeMemberField(_codeDomHelper.GetGlobalizedTypeName(typeof(ITestRunner)),
                GeneratorConstants.TESTRUNNER_FIELD);
            type.Members.Add(testRunnerField);
            return testRunnerField;
        }

        public void SetupTestClass(TestClassGenerationContext generationContext)
        {
            generationContext.TestClass.IsPartial = true;
            generationContext.TestClass.TypeAttributes |= TypeAttributes.Public;
            _codeDomHelper.AddLinePragmaInitial(generationContext.TestClass, generationContext.Document.SourceFilePath,
                _specFlowConfiguration);
            _testGeneratorProvider.SetTestClass(generationContext, generationContext.Feature.Name,
                generationContext.Feature.Description);
            _decoratorRegistry.DecorateTestClass(generationContext, out List<string> unprocessedTags);
            if (unprocessedTags.Any())
            {
                _testGeneratorProvider.SetTestClassCategories(generationContext, unprocessedTags);
            }

            DeclareFeatureTagsField(generationContext);
            DeclareFeatureInfoMember(generationContext);
        }

        private void DeclareFeatureTagsField(TestClassGenerationContext generationContext)
        {
            var featureTagsField = new CodeMemberField(typeof(string[]), GeneratorConstants.FEATURE_TAGS_VARIABLE_NAME);
            featureTagsField.Attributes |= MemberAttributes.Static;
            featureTagsField.InitExpression =
                _scenarioPartHelper.GetStringArrayExpression(generationContext.Feature.Tags);
            generationContext.TestClass.Members.Add(featureTagsField);
        }

        private void DeclareFeatureInfoMember(TestClassGenerationContext generationContext)
        {
            var featureInfoField = new CodeMemberField(
                _codeDomHelper.GetGlobalizedTypeName(typeof(FeatureInfo)), GeneratorConstants.FEATUREINFO_FIELD);
            featureInfoField.Attributes |= MemberAttributes.Static;
            featureInfoField.InitExpression = new CodeObjectCreateExpression(
                _codeDomHelper.GetGlobalizedTypeName(typeof(FeatureInfo)),
                new CodeObjectCreateExpression(typeof(CultureInfo),
                    new CodePrimitiveExpression(generationContext.Feature.Language)),
                new CodePrimitiveExpression(generationContext.Document.DocumentLocation?.FeatureFolderPath),
                new CodePrimitiveExpression(generationContext.Feature.Name),
                new CodePrimitiveExpression(generationContext.Feature.Description),
                new CodeFieldReferenceExpression(
                    new CodeTypeReferenceExpression(_codeDomHelper.GetGlobalizedTypeName(typeof(ProgrammingLanguage))),
                    _codeDomHelper.TargetLanguage.ToString()),
                new CodeFieldReferenceExpression(null, GeneratorConstants.FEATURE_TAGS_VARIABLE_NAME));

            generationContext.TestClass.Members.Add(featureInfoField);
        }

        public void SetupTestClassInitializeMethod(TestClassGenerationContext generationContext)
        {
            var initializeMethod = generationContext.TestClassInitializeMethod;
            initializeMethod.Attributes = MemberAttributes.Public;
            initializeMethod.Name = "FeatureSetupAsync";
            _codeDomHelper.MarkCodeMemberMethodAsAsync(initializeMethod);
            _testGeneratorProvider.SetTestClassInitializeMethod(generationContext);
        }

        public void SetupTestInitializeMethod(TestClassGenerationContext generationContext)
        {
            var initializeMethod = generationContext.TestInitializeMethod;
            initializeMethod.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            initializeMethod.Name = "TestInitializeAsync";

            // Mark the method as async
            _codeDomHelper.MarkCodeMemberMethodAsAsync(initializeMethod);

            // Add the [NUnit.Framework.SetUpAttribute()]
            _codeDomHelper.AddAttribute(initializeMethod, "NUnit.Framework.SetUpAttribute");

            // Define the testRunner field
            var testRunnerField = _scenarioPartHelper.GetTestRunnerExpression();

            // Add: if (testRunner == null)
            var getTestRunnerExpression = new CodeMethodInvokeExpression(
                new CodeTypeReferenceExpression(_codeDomHelper.GetGlobalizedTypeName(typeof(TestRunnerManager))),
                nameof(TestRunnerManager.GetTestRunnerForAssembly),
                _codeDomHelper.CreateOptionalArgumentExpression(
                    "featureHint",
                    new CodeVariableReferenceExpression(GeneratorConstants.FEATUREINFO_FIELD)));
            var initializeTestRunnerStatement = new CodeConditionStatement(
                new CodeBinaryOperatorExpression(
                    testRunnerField,
                    CodeBinaryOperatorType.IdentityEquality,
                    new CodePrimitiveExpression(null)),
                new CodeStatement[]
                {
                    new CodeAssignStatement(testRunnerField, getTestRunnerExpression),
                    new CodeConditionStatement(
                        new CodeBinaryOperatorExpression(
                            testRunnerField,
                            CodeBinaryOperatorType.IdentityEquality,
                            new CodePrimitiveExpression(null)),
                        new CodeThrowExceptionStatement(
                            new CodeObjectCreateExpression(
                                typeof(InvalidOperationException),
                                new CodePrimitiveExpression(
                                    "Failed to initialize testRunner. Ensure TestRunnerManager is properly configured."))))
                });

            initializeMethod.Statements.Add(initializeTestRunnerStatement);

            // Add: if (testRunner.FeatureContext != null)
            var featureContextExpression = new CodePropertyReferenceExpression(testRunnerField, "FeatureContext");
            var featureInfoExpression = new CodePropertyReferenceExpression(featureContextExpression, "FeatureInfo");
            var onFeatureEndAsyncExpression =
                new CodeMethodInvokeExpression(testRunnerField, nameof(ITestRunner.OnFeatureEndAsync));
            _codeDomHelper.MarkCodeMethodInvokeExpressionAsAwait(onFeatureEndAsyncExpression);

            var featureContextCheckStatement = new CodeConditionStatement(
                new CodeBinaryOperatorExpression(
                    featureContextExpression,
                    CodeBinaryOperatorType.IdentityInequality,
                    new CodePrimitiveExpression(null)),
                new CodeStatement[]
                {
                    new CodeConditionStatement(
                        new CodeBinaryOperatorExpression(
                            new CodeBinaryOperatorExpression(
                                featureInfoExpression,
                                CodeBinaryOperatorType.IdentityInequality,
                                new CodePrimitiveExpression(null)),
                            CodeBinaryOperatorType.BooleanAnd,
                            new CodeBinaryOperatorExpression(
                                new CodeMethodInvokeExpression(
                                    featureInfoExpression,
                                    nameof(object.Equals),
                                    new CodeVariableReferenceExpression(GeneratorConstants.FEATUREINFO_FIELD)),
                                CodeBinaryOperatorType.ValueEquality,
                                new CodePrimitiveExpression(false))),
                        new CodeExpressionStatement(onFeatureEndAsyncExpression))
                });

            initializeMethod.Statements.Add(featureContextCheckStatement);

            // Add: if (testRunner.FeatureContext == null)
            var onFeatureStartExpression = new CodeMethodInvokeExpression(
                testRunnerField,
                nameof(ITestRunner.OnFeatureStartAsync),
                new CodeVariableReferenceExpression(GeneratorConstants.FEATUREINFO_FIELD));
            _codeDomHelper.MarkCodeMethodInvokeExpressionAsAwait(onFeatureStartExpression);

            var featureContextNullCheckStatement = new CodeConditionStatement(
                new CodeBinaryOperatorExpression(
                    featureContextExpression,
                    CodeBinaryOperatorType.IdentityEquality,
                    new CodePrimitiveExpression(null)),
                new CodeExpressionStatement(onFeatureStartExpression));

            initializeMethod.Statements.Add(featureContextNullCheckStatement);
        }

        public void SetupTestCleanupMethod(TestClassGenerationContext generationContext)
        {
            var testCleanupMethod = generationContext.TestCleanupMethod;
            testCleanupMethod.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            testCleanupMethod.Name = "TestTearDownAsync";

            // Mark the method as async
            _codeDomHelper.MarkCodeMemberMethodAsAsync(testCleanupMethod);

            // Add the [NUnit.Framework.TearDownAttribute()]
            _codeDomHelper.AddAttribute(testCleanupMethod, "NUnit.Framework.TearDownAttribute");

            // Define the testRunner field
            var testRunnerField = _scenarioPartHelper.GetTestRunnerExpression();

            // Add: if (testRunner?.ScenarioContext != null)
            var scenarioContextProperty = new CodePropertyReferenceExpression(testRunnerField, "ScenarioContext");
            var conditionExpression = new CodeBinaryOperatorExpression(
                scenarioContextProperty,
                CodeBinaryOperatorType.IdentityInequality,
                new CodePrimitiveExpression(null));

            // Inside the if: await testRunner.OnScenarioEndAsync();
            var onScenarioEndAsyncExpression = new CodeMethodInvokeExpression(
                testRunnerField,
                nameof(ITestRunner.OnScenarioEndAsync));
            _codeDomHelper.MarkCodeMethodInvokeExpressionAsAwait(onScenarioEndAsyncExpression);

            var conditionStatement = new CodeConditionStatement(
                conditionExpression,
                new CodeExpressionStatement(onScenarioEndAsyncExpression));

            testCleanupMethod.Statements.Add(conditionStatement);

            // Add: TestRunnerManager.ReleaseTestRunner(testRunner);
            var releaseTestRunnerExpression = new CodeMethodInvokeExpression(
                new CodeTypeReferenceExpression(_codeDomHelper.GetGlobalizedTypeName(typeof(TestRunnerManager))),
                nameof(TestRunnerManager.ReleaseTestRunner),
                testRunnerField);

            testCleanupMethod.Statements.Add(new CodeExpressionStatement(releaseTestRunnerExpression));

            // Add: testRunner = null;
            testCleanupMethod.Statements.Add(
                new CodeAssignStatement(
                    testRunnerField,
                    new CodePrimitiveExpression(null)));
        }

        public void SetupTestClassCleanupMethod(TestClassGenerationContext generationContext)
        {
            var classCleanupMethod = generationContext.TestClassCleanupMethod;
            classCleanupMethod.Attributes = MemberAttributes.Public;
            classCleanupMethod.Name = "FeatureTearDownAsync";
            _codeDomHelper.MarkCodeMemberMethodAsAsync(classCleanupMethod);
            _testGeneratorProvider.SetTestClassCleanupMethod(generationContext);
        }

        protected CodeExpression GetTestRunnerExpression()
        {
            return new CodeVariableReferenceExpression("testRunner");
        }
    }
}