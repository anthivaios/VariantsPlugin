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
        private readonly ReqnrollConfiguration _reqnrollConfiguration;
        private readonly ScenarioPartHelper _scenarioPartHelper;

        public TestClassGenerator(IDecoratorRegistry decoratorRegistry,
            IUnitTestGeneratorProvider testGeneratorProvider, CodeDomHelper codeDomHelper,
            ReqnrollConfiguration reqnrollConfiguration)
        {
            _decoratorRegistry = decoratorRegistry;
            _testGeneratorProvider = testGeneratorProvider;
            _codeDomHelper = codeDomHelper;
            _reqnrollConfiguration = reqnrollConfiguration;
            _scenarioPartHelper = new ScenarioPartHelper(_reqnrollConfiguration, _codeDomHelper);
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
                _reqnrollConfiguration.AllowRowTests);
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
                _reqnrollConfiguration);
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

            _testGeneratorProvider.SetTestInitializeMethod(generationContext);

             var testRunnerField = _scenarioPartHelper.GetTestRunnerExpression();

            var getTestRunnerExpression = new CodeMethodInvokeExpression(
                new CodeTypeReferenceExpression(_codeDomHelper.GetGlobalizedTypeName(typeof(TestRunnerManager))),
                nameof(TestRunnerManager.GetTestRunnerForAssembly),
                _codeDomHelper.CreateOptionalArgumentExpression("featureHint", 
                    new CodeVariableReferenceExpression(GeneratorConstants.FEATUREINFO_FIELD)));

            initializeMethod.Statements.Add(
                new CodeAssignStatement(
                    testRunnerField,
                    getTestRunnerExpression));


            // "Finish" current feature if needed

            var featureContextExpression = new CodePropertyReferenceExpression(
                testRunnerField,
                "FeatureContext");

            var onFeatureEndAsyncExpression = new CodeMethodInvokeExpression(
                testRunnerField,
                nameof(ITestRunner.OnFeatureEndAsync));
            _codeDomHelper.MarkCodeMethodInvokeExpressionAsAwait(onFeatureEndAsyncExpression);

            //if (testRunner.FeatureContext != null && !testRunner.FeatureContext.FeatureInfo.Equals(featureInfo))
            //  await testRunner.OnFeatureEndAsync(); // finish if different
            initializeMethod.Statements.Add(
                new CodeConditionStatement(
                    new CodeBinaryOperatorExpression(
                        new CodeBinaryOperatorExpression(
                            featureContextExpression,
                            CodeBinaryOperatorType.IdentityInequality,
                            new CodePrimitiveExpression(null)),
                        CodeBinaryOperatorType.BooleanAnd,
                        new CodeBinaryOperatorExpression(
                            new CodeMethodInvokeExpression(
                                new CodePropertyReferenceExpression(
                                    featureContextExpression,
                                    "FeatureInfo"),
                                nameof(object.Equals),
                                new CodeVariableReferenceExpression(GeneratorConstants.FEATUREINFO_FIELD)),
                            CodeBinaryOperatorType.ValueEquality,
                            new CodePrimitiveExpression(false))),
                    new CodeExpressionStatement(
                        onFeatureEndAsyncExpression)));


            // "Start" the feature if needed

            //if (testRunner.FeatureContext == null) {
            //  await testRunner.OnFeatureStartAsync(featureInfo);
            //}

            var onFeatureStartExpression = new CodeMethodInvokeExpression(
                testRunnerField,
                nameof(ITestRunner.OnFeatureStartAsync),
                new CodeVariableReferenceExpression(GeneratorConstants.FEATUREINFO_FIELD));
            _codeDomHelper.MarkCodeMethodInvokeExpressionAsAwait(onFeatureStartExpression);

            initializeMethod.Statements.Add(
                new CodeConditionStatement(
                    new CodeBinaryOperatorExpression(
                        featureContextExpression,
                        CodeBinaryOperatorType.IdentityEquality,
                        new CodePrimitiveExpression(null)),
                    new CodeExpressionStatement(
                        onFeatureStartExpression)));
        }

        public void SetupTestCleanupMethod(TestClassGenerationContext generationContext)
        {
            var testCleanupMethod = generationContext.TestCleanupMethod;
            testCleanupMethod.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            testCleanupMethod.Name = "TestTearDownAsync";

            // Mark the method as async
            _codeDomHelper.MarkCodeMemberMethodAsAsync(testCleanupMethod);

            _testGeneratorProvider.SetTestCleanupMethod(generationContext);

            var testRunnerField = _scenarioPartHelper.GetTestRunnerExpression();
            
            //await testRunner.OnScenarioEndAsync();
            var expression = new CodeMethodInvokeExpression(
                testRunnerField,
                nameof(ITestRunner.OnScenarioEndAsync));

            _codeDomHelper.MarkCodeMethodInvokeExpressionAsAwait(expression);

            testCleanupMethod.Statements.Add(expression);

            // "Release" the TestRunner, so that other threads can pick it up
            // TestRunnerManager.ReleaseTestRunner(testRunner);
            testCleanupMethod.Statements.Add(
                new CodeMethodInvokeExpression(
                    new CodeTypeReferenceExpression(_codeDomHelper.GetGlobalizedTypeName(typeof(TestRunnerManager))),
                    nameof(TestRunnerManager.ReleaseTestRunner),
                    testRunnerField));
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