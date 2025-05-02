using System.CodeDom;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using Gherkin.Ast;
using Reqnroll;
using Reqnroll.Configuration;
using Reqnroll.Generator;
using Reqnroll.Generator.CodeDom;
using Reqnroll.Generator.Generation;
using Reqnroll.Generator.UnitTestConverter;
using Reqnroll.Generator.UnitTestProvider;
using Reqnroll.Parser;
using Reqnroll.Tracing;

namespace VariantsPlugin
{
    public class FeatureGeneratorExtended : TestClassGenerator, IFeatureGenerator
    {
        private readonly IUnitTestGeneratorProvider _testGeneratorProvider;
        private readonly CodeDomHelper _codeDomHelper;
        private readonly ReqnrollConfiguration _reqnrollConfiguration;
        private readonly IDecoratorRegistry _decoratorRegistry;
        private int _tableCounter;

        //NEW CODE START
        private readonly VariantHelper _variantHelper;
        private List<Tag> _featureVariantTags;
        private bool _setVariantToContextForOutlineTest;
        private bool _setVariantToContextForTest;
        private string _variantValue;
        readonly ScenarioPartHelper _scenarioPartHelper;
        public const string CustomGeneratedComment = "Generation customised by VariantPlugin";

        public bool IsRetryActive;
        //NEW CODE END

        public FeatureGeneratorExtended(IUnitTestGeneratorProvider testGeneratorProvider, CodeDomHelper codeDomHelper,
            ReqnrollConfiguration reqnrollConfiguration, IDecoratorRegistry decoratorRegistry, string variantKey, bool isRetryActive)
            : base(decoratorRegistry, testGeneratorProvider, codeDomHelper, reqnrollConfiguration)
        {
            _testGeneratorProvider = testGeneratorProvider;
            _codeDomHelper = codeDomHelper;
            _reqnrollConfiguration = reqnrollConfiguration;
            _decoratorRegistry = decoratorRegistry;
            _scenarioPartHelper = new ScenarioPartHelper(_reqnrollConfiguration, _codeDomHelper);
            _variantHelper = new VariantHelper(variantKey); //NEW CODE
            IsRetryActive = isRetryActive;
        }

        public CodeNamespace GenerateUnitTestFixture(ReqnrollDocument document, string testClassName,
            string targetNamespace)
        {
            var reqnrollFeature = document.ReqnrollFeature;
            testClassName = testClassName ?? $"{reqnrollFeature.Name.ToIdentifier()}Feature";
            var codeNamespace = CreateNamespace(targetNamespace);
            var generationContext = CreateTestClassStructure(codeNamespace, testClassName, document);

            SetupTestClass(generationContext);
            SetupTestClassInitializeMethod(generationContext);
            SetupTestClassCleanupMethod(generationContext);

            SetupScenarioStartMethod(generationContext);
            SetupScenarioInitializeMethod(generationContext);
            _scenarioPartHelper.SetupFeatureBackground(generationContext);
            SetupScenarioCleanupMethod(generationContext);

            SetupTestInitializeMethod(generationContext);
            SetupTestCleanupMethod(generationContext);


            //NEW CODE START
            var variantTags = _variantHelper.GetFeatureVariantTagValues(reqnrollFeature);
            _featureVariantTags = _variantHelper.FeatureTags(reqnrollFeature);

            if (_variantHelper.AnyScenarioHasVariantTag(reqnrollFeature) && _variantHelper.FeatureHasVariantTags)
                throw new TestGeneratorException(
                    "Variant tags were detected at feature and scenario level, please specify at one level or the other.");
            //NEW CODE END

            foreach (var scenarioDefinition in GetScenarioDefinitions(reqnrollFeature))
            {
                if (string.IsNullOrEmpty(scenarioDefinition.Scenario.Name))
                    throw new TestGeneratorException("The scenario must have a title specified.");

                if (scenarioDefinition.IsScenarioOutline)
                {
                    //NEW CODE START
                    variantTags = _variantHelper.FeatureHasVariantTags
                        ? variantTags
                        : _variantHelper.GetScenarioVariantTagValues(scenarioDefinition.ScenarioDefinition);
                    GenerateScenarioOutlineTest(generationContext, scenarioDefinition, variantTags);
                }
                else
                {
                    variantTags = _variantHelper.FeatureHasVariantTags
                        ? variantTags
                        : _variantHelper.GetScenarioVariantTagValues(scenarioDefinition.ScenarioDefinition);
                    if (variantTags.Count > 0)
                    {
                        variantTags.ForEach(a =>
                            GenerateTest(generationContext, (ScenarioDefinitionInFeatureFile)scenarioDefinition, a));
                    }
                    else
                    {
                        GenerateTest(generationContext, (ScenarioDefinitionInFeatureFile)scenarioDefinition, null);
                    }
                    //NEW CODE END
                }
            }

            _testGeneratorProvider.FinalizeTestClass(generationContext);
            codeNamespace.Comments.Add(new CodeCommentStatement(new CodeComment(CustomGeneratedComment))); //NEW CODE
            return codeNamespace;
        }

        private IEnumerable<ScenarioDefinitionInFeatureFile> GetScenarioDefinitions(ReqnrollFeature feature)
        {
            IEnumerable<ScenarioDefinitionInFeatureFile> GetScenarioDefinitionsOfRule(IEnumerable<IHasLocation> items,
                Rule rule)
                => items.OfType<StepsContainer>()
                    .Where(child => child is not Background)
                    .Select(sd => new ScenarioDefinitionInFeatureFile(sd, feature, rule));

            return
                GetScenarioDefinitionsOfRule(feature.Children, null)
                    .Concat(feature.Children.OfType<Rule>()
                        .SelectMany(rule => GetScenarioDefinitionsOfRule(rule.Children, rule)));
        }

        private void SetupScenarioCleanupMethod(TestClassGenerationContext generationContext)
        {
            var scenarioCleanupMethod = generationContext.ScenarioCleanupMethod;
            scenarioCleanupMethod.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            scenarioCleanupMethod.Name = "ScenarioCleanupAsync";
            _codeDomHelper.MarkCodeMemberMethodAsAsync(scenarioCleanupMethod);
            var testRunnerField = _scenarioPartHelper.GetTestRunnerExpression();

            //await testRunner.CollectScenarioErrorsAsync();
            var expression = new CodeMethodInvokeExpression(
                testRunnerField,
                nameof(ITestRunner.CollectScenarioErrorsAsync));

            _codeDomHelper.MarkCodeMethodInvokeExpressionAsAwait(expression);

            scenarioCleanupMethod.Statements.Add(expression);
        }

        private void SetupScenarioStartMethod(TestClassGenerationContext generationContext)
        {
            var scenarioStartMethod = generationContext.ScenarioStartMethod;
            scenarioStartMethod.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            scenarioStartMethod.Name = GeneratorConstants.SCENARIO_START_NAME;
            _codeDomHelper.MarkCodeMemberMethodAsAsync(scenarioStartMethod);

            //await testRunner.OnScenarioStartAsync();
            var testRunnerField = _scenarioPartHelper.GetTestRunnerExpression();
            var expression = new CodeMethodInvokeExpression(
                testRunnerField,
                nameof(ITestRunner.OnScenarioStartAsync));

            _codeDomHelper.MarkCodeMethodInvokeExpressionAsAwait(expression);

            scenarioStartMethod.Statements.Add(expression);
        }

        private void SetupFeatureBackground(TestClassGenerationContext generationContext)
        {
            if (!generationContext.Feature.HasFeatureBackground())
                return;
            var backgroundMethod = generationContext.FeatureBackgroundMethod;
            backgroundMethod.Attributes = MemberAttributes.Public;
            backgroundMethod.Name = "FeatureBackgroundAsync";
            _codeDomHelper.MarkCodeMemberMethodAsAsync(backgroundMethod);
            var background = generationContext.Feature.Background;
            _codeDomHelper.AddLineDirective(background, backgroundMethod.Statements, _reqnrollConfiguration);
            var statements = new List<CodeStatement>();
            using (new SourceLineScope(_reqnrollConfiguration, _codeDomHelper, statements,
                       generationContext.Document.SourceFilePath, background.Location))
            {
            }

            foreach (var step in background.Steps)
                GenerateStep(generationContext, statements, step, null);
            _codeDomHelper.AddLineDirectiveHidden(backgroundMethod.Statements, _reqnrollConfiguration);
        }

        private void SetupScenarioInitializeMethod(TestClassGenerationContext generationContext)
        {
            var initializeMethod = generationContext.ScenarioInitializeMethod;
            initializeMethod.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            initializeMethod.Name = "ScenarioInitialize";
            initializeMethod.Parameters.Add(
                new CodeParameterDeclarationExpression(_codeDomHelper.GetGlobalizedTypeName(typeof(ScenarioInfo)),
                    "scenarioInfo"));

            //testRunner.OnScenarioInitialize(scenarioInfo);
            var testRunnerField = _scenarioPartHelper.GetTestRunnerExpression();
            initializeMethod.Statements.Add(
                new CodeMethodInvokeExpression(
                    testRunnerField,
                    nameof(ITestRunner.OnScenarioInitialize),
                    new CodeVariableReferenceExpression("scenarioInfo")));
        }

        private void GenerateScenarioOutlineTest(TestClassGenerationContext generationContext,
            ScenarioDefinitionInFeatureFile scenarioDefinitionInFeatureFile, List<string> variantTags = null)
        {
            var scenarioOutline = scenarioDefinitionInFeatureFile.ScenarioOutline;
            ValidateExampleSetConsistency(scenarioOutline);

            var paramToIdentifier = CreateParamToIdentifierMapping(scenarioOutline);

            var scenarioOutlineTestMethod =
                CreateScenarioOutlineTestMethod(generationContext, scenarioOutline, paramToIdentifier);
            var exampleTagsParam =
                new CodeVariableReferenceExpression(GeneratorConstants.SCENARIO_OUTLINE_EXAMPLE_TAGS_PARAMETER);

            //NEW CODE START
            if (generationContext.GenerateRowTests)
            {
                if (variantTags?.Count > 0)
                    GenerateScenarioOutlineExamplesAsRowTests(generationContext, scenarioOutline,
                        scenarioOutlineTestMethod, variantTags);
                else
                    GenerateScenarioOutlineExamplesAsRowTests(generationContext, scenarioOutline,
                        scenarioOutlineTestMethod, null);
            }
            else
            {
                if (variantTags?.Count > 0)
                    variantTags.ForEach(a => GenerateScenarioOutlineExamplesAsIndividualMethods(scenarioOutline,
                        generationContext, scenarioOutlineTestMethod, paramToIdentifier, a));
                else
                    GenerateScenarioOutlineExamplesAsIndividualMethods(scenarioOutline, generationContext,
                        scenarioOutlineTestMethod, paramToIdentifier, null);
            }
            //NEW CODE END
            
            GenerateTestBody(generationContext, scenarioDefinitionInFeatureFile, scenarioOutlineTestMethod,
                exampleTagsParam, paramToIdentifier);
        }

        private ParameterSubstitution CreateParamToIdentifierMapping(ScenarioOutline scenarioOutline)
        {
            var paramToIdentifier = new ParameterSubstitution();
            paramToIdentifier.Add("example", "example".ToIdentifierCamelCase());
            foreach (var param in scenarioOutline.Examples.First().TableHeader.Cells)
            {
                paramToIdentifier.Add(param.Value, param.Value.ToIdentifierCamelCase());
            }

            //fix empty parameters
            var emptyStrings = paramToIdentifier.Where(kv => kv.Value == string.Empty).ToArray();
            foreach (var item in emptyStrings)
            {
                paramToIdentifier.Remove(item);
                paramToIdentifier.Add(item.Key, "_");
            }

            //fix duplicated parameter names
            for (int i = 0; i < paramToIdentifier.Count; i++)
            {
                int suffix = 1;
                while (paramToIdentifier.Take(i).Count(kv => kv.Value == paramToIdentifier[i].Value) > 0)
                {
                    paramToIdentifier[i] = new KeyValuePair<string, string>(paramToIdentifier[i].Key,
                        paramToIdentifier[i].Value + suffix);
                    suffix++;
                }
            }


            return paramToIdentifier;
        }

        private void ValidateExampleSetConsistency(ScenarioOutline scenarioOutline)
        {
            if (scenarioOutline.Examples.Count() <= 1)
            {
                return;
            }

            var firstExamplesHeader = scenarioOutline.Examples.First().TableHeader.Cells.Select(c => c.Value).ToArray();

            //check params
            if (scenarioOutline.Examples
                .Skip(1)
                .Select(examples => examples.TableHeader.Cells.Select(c => c.Value))
                .Any(paramNames => !paramNames.SequenceEqual(firstExamplesHeader)))
            {
                throw new TestGeneratorException("The example sets must provide the same parameters.");
            }
        }

        private void GenerateScenarioOutlineExamplesAsIndividualMethods(ScenarioOutline scenarioOutline,
            TestClassGenerationContext generationContext, CodeMemberMethod scenatioOutlineTestMethod,
            ParameterSubstitution paramToIdentifier, string tag = null)
        {
            int num = 0;
            foreach (var example in scenarioOutline.Examples)
            {
                var flag = example.TableBody.CanUseFirstColumnAsName();
                string str;
                if (!string.IsNullOrEmpty(example.Name))
                {
                    str = example.Name.ToIdentifier();
                }
                else
                {
                    var examples = scenarioOutline.Examples;
                    bool func(Examples es) => string.IsNullOrEmpty(es.Name);
                    str = examples.Count(func) > 1 ? $"ExampleSet {num}".ToIdentifier() : null;
                }

                foreach (var data in example.TableBody.Select((r, i) => new
                         {
                             Row = r,
                             Index = i
                         }))
                {
                    var variantName = flag ? data.Row.Cells.First().Value : $"Variant {data.Index}";
                    GenerateScenarioOutlineTestVariant(generationContext, scenarioOutline, scenatioOutlineTestMethod,
                        paramToIdentifier, example.Name ?? "", str, data.Row, example.Tags, variantName, tag);
                }

                num++;
            }
        }

        private void GenerateScenarioOutlineExamplesAsRowTests(TestClassGenerationContext generationContext,
            ScenarioOutline scenarioOutline, CodeMemberMethod scenatioOutlineTestMethod,
            List<string> variantTags = null)
        {
            SetupTestMethod(generationContext, scenatioOutlineTestMethod, scenarioOutline, null, null, null, true);
            foreach (var example in scenarioOutline.Examples)
            {
                //NEW CODE START
                var hasVariantTags = variantTags?.Count > 0;
                if (hasVariantTags)
                {
                    scenatioOutlineTestMethod.Parameters.RemoveAt(scenatioOutlineTestMethod.Parameters.Count - 1);
                    scenatioOutlineTestMethod.Parameters.Add(new CodeParameterDeclarationExpression(typeof(string),
                        _variantHelper.VariantKey.ToLowerInvariant()));
                    scenatioOutlineTestMethod.Parameters.Add(
                        new CodeParameterDeclarationExpression(typeof(string[]), "exampleTags"));
                    _setVariantToContextForOutlineTest = true;
                }

                int count = 1;
                foreach (var tableRow in example.TableBody)
                {
                    var exampleList = new List<string>() { $"Example {count++}" };
                    if (hasVariantTags)
                    {
                        foreach (var variant in variantTags)
                        {
                            var arguments = tableRow.Cells.Select(c => c.Value).ToList();
                            arguments.Add($"{variant}");
                            exampleList.AddRange(arguments);
                            _testGeneratorProvider.SetRow(generationContext, scenatioOutlineTestMethod, exampleList ,
                                example.Tags.GetTagsExcept("@Ignore"), example.Tags.HasTag("@Ignore"));
                        }
                    }
                    else
                    {
                        var arguments = tableRow.Cells.Select(c => c.Value).ToList();
                        exampleList.AddRange(arguments);
                        _testGeneratorProvider.SetRow(generationContext, scenatioOutlineTestMethod, exampleList,
                            example.Tags.GetTagsExcept("@Ignore"), example.Tags.HasTag("@Ignore"));
                    }
                    //NEW CODE END
                }
            }
        }

        private CodeMemberMethod CreateScenarioOutlineTestMethod(TestClassGenerationContext generationContext,
            ScenarioOutline scenarioOutline, ParameterSubstitution paramToIdentifier)
        {
            var method = generationContext.TestClass.CreateMethod();
            method.Attributes = MemberAttributes.Public;
            method.Name = scenarioOutline.Name.ToIdentifier();
            foreach (var keyValuePair in paramToIdentifier)
                method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(string), keyValuePair.Value));
            method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(string[]), "exampleTags"));
            _codeDomHelper.MarkCodeMemberMethodAsAsync(method);
            return method;
        }

        private void GenerateScenarioOutlineTestVariant(TestClassGenerationContext generationContext,
            ScenarioOutline scenarioOutline, CodeMemberMethod scenatioOutlineTestMethod,
            IEnumerable<KeyValuePair<string, string>> paramToIdentifier, string exampleSetTitle,
            string exampleSetIdentifier, Gherkin.Ast.TableRow row, IEnumerable<Tag> exampleSetTags, string variantName,
            string tag = null)
        {
            variantName = string.IsNullOrEmpty(tag) ? variantName : $"{variantName}_{tag}";
            var testMethod = CreateTestMethod(generationContext, scenarioOutline, exampleSetTags, variantName,
                exampleSetIdentifier);
            _codeDomHelper.AddLineDirective(scenarioOutline, testMethod.Statements, _reqnrollConfiguration);
            var list1 = new List<CodeExpression>();
            list1.AddRange(row.Cells.Select(paramCell => new CodePrimitiveExpression(paramCell.Value))
                .Cast<CodeExpression>().ToList());
            list1.Add(exampleSetTags.GetStringArrayExpression());

            //// NEW CODE START
            if (tag != null)
            {
                var s = new CodePrimitiveExpression(tag);
                list1.Add(s);
                _setVariantToContextForOutlineTest = true;
            }
            //// NEW CODE END

            testMethod.Statements.Add(new CodeMethodInvokeExpression(new CodeThisReferenceExpression(),
                scenatioOutlineTestMethod.Name, list1.ToArray()));
            _codeDomHelper.AddLineDirectiveHidden(testMethod.Statements, _reqnrollConfiguration);
            var list2 = paramToIdentifier.Select((p2i, paramIndex) =>
                new KeyValuePair<string, string>(p2i.Key, row.Cells.ElementAt(paramIndex).Value)).ToList();
            _testGeneratorProvider.SetTestMethodAsRow(generationContext, testMethod, scenarioOutline.Name,
                exampleSetTitle, variantName, list2);
        }

        private CodeMemberMethod CreateTestMethod(TestClassGenerationContext generationContext, StepsContainer scenario,
            IEnumerable<Tag> additionalTags, string variantName = null, string exampleSetIdentifier = null)
        {
            var method = generationContext.TestClass.CreateMethod();
            _codeDomHelper.MarkCodeMemberMethodAsAsync(method);
            SetupTestMethod(generationContext, method, scenario, additionalTags, variantName, exampleSetIdentifier,
                false);
            return method;
        }

        private void GenerateTest(TestClassGenerationContext generationContext,
            ScenarioDefinitionInFeatureFile scenarioDefinitionInFeatureFile, string tag = null)
        {
            // NEW CODE START
            string variantName = null;
            if (!string.IsNullOrEmpty(tag))
            {
                variantName = $"__{tag}";
                _setVariantToContextForTest = true;
                _variantValue = tag;
            }
            // NEW CODE END

            var testMethod = CreateTestMethod(generationContext, scenarioDefinitionInFeatureFile.ScenarioDefinition,
                null, variantName, null);
            GenerateTestBody(generationContext, scenarioDefinitionInFeatureFile, testMethod, null, null);
        }

        private void GenerateTestBody(TestClassGenerationContext generationContext,
            ScenarioDefinitionInFeatureFile scenarioDefinitionInFeatureFile, CodeMemberMethod testMethod,
            CodeExpression additionalTagsExpression = null, ParameterSubstitution paramToIdentifier = null)
        {
            var scenarioDefinition = scenarioDefinitionInFeatureFile.ScenarioDefinition;
            var feature = scenarioDefinitionInFeatureFile.Feature;

            //call test setup
            //ScenarioInfo scenarioInfo = new ScenarioInfo("xxxx", tags...);
            CodeExpression inheritedTagsExpression;
            var featureTagsExpression =
                new CodeFieldReferenceExpression(null, GeneratorConstants.FEATURE_TAGS_VARIABLE_NAME);
            if (scenarioDefinitionInFeatureFile.Rule != null && scenarioDefinitionInFeatureFile.Rule.Tags.Any())
            {
                var tagHelperReference = new CodeTypeReferenceExpression(nameof(TagHelper));
                var ruleTagsExpression =
                    _scenarioPartHelper.GetStringArrayExpression(scenarioDefinitionInFeatureFile.Rule.Tags);
                inheritedTagsExpression = new CodeMethodInvokeExpression(tagHelperReference,
                    nameof(TagHelper.CombineTags), featureTagsExpression, ruleTagsExpression);
            }
            else
            {
                inheritedTagsExpression = featureTagsExpression;
            }

            CodeExpression tagsExpression;
            if (additionalTagsExpression == null)
            {
                tagsExpression = _scenarioPartHelper.GetStringArrayExpression(scenarioDefinition.GetTags());
            }
            else if (!scenarioDefinition.HasTags())
            {
                tagsExpression = additionalTagsExpression;
            }
            else
            {
                // merge tags list
                // var tags = tags1
                // if (tags2 != null)
                //   tags = Enumerable.ToArray(Enumerable.Concat(tags1, tags1));
                testMethod.Statements.Add(
                    new CodeVariableDeclarationStatement(typeof(string[]), "__tags",
                        _scenarioPartHelper.GetStringArrayExpression(scenarioDefinition.GetTags())));
                tagsExpression = new CodeVariableReferenceExpression("__tags");
                testMethod.Statements.Add(
                    new CodeConditionStatement(
                        new CodeBinaryOperatorExpression(
                            additionalTagsExpression,
                            CodeBinaryOperatorType.IdentityInequality,
                            new CodePrimitiveExpression(null)),
                        new CodeAssignStatement(
                            tagsExpression,
                            new CodeMethodInvokeExpression(
                                new CodeTypeReferenceExpression(typeof(Enumerable)),
                                "ToArray",
                                new CodeMethodInvokeExpression(
                                    new CodeTypeReferenceExpression(typeof(Enumerable)),
                                    "Concat",
                                    tagsExpression,
                                    additionalTagsExpression)))));
            }


            AddVariableForTags(testMethod, tagsExpression);

            AddVariableForArguments(testMethod, paramToIdentifier);
            var scenarioName = scenarioDefinition.Name;
            if (_variantValue != null)
            {
                scenarioName = scenarioName + $": {_variantValue}";
            }

            if (paramToIdentifier == null)
            {
                testMethod.Statements.Add(
                    new CodeVariableDeclarationStatement(_codeDomHelper.GetGlobalizedTypeName(typeof(ScenarioInfo)),
                        "scenarioInfo",
                        new CodeObjectCreateExpression(_codeDomHelper.GetGlobalizedTypeName(typeof(ScenarioInfo)),
                            new CodePrimitiveExpression(scenarioName),
                            new CodePrimitiveExpression(scenarioDefinition.Description),
                            new CodeVariableReferenceExpression(GeneratorConstants.SCENARIO_TAGS_VARIABLE_NAME),
                            new CodeVariableReferenceExpression(GeneratorConstants.SCENARIO_ARGUMENTS_VARIABLE_NAME),
                            inheritedTagsExpression)));

            }
            else
            {
                var hasOperatorVariable = scenarioDefinition.GetTags()
                    .Any(c => c.GetNameWithoutAt().StartsWith($"{_variantHelper.VariantKey}:"));
                
                
                if (hasOperatorVariable)
                {
                    testMethod.Statements.Add(
                        new CodeVariableDeclarationStatement(
                            _codeDomHelper.GetGlobalizedTypeName(typeof(ScenarioInfo)), 
                            "scenarioInfo", 
                            new CodeObjectCreateExpression(
                                _codeDomHelper.GetGlobalizedTypeName(typeof(ScenarioInfo)),
                                new CodeBinaryOperatorExpression(
                                    new CodeBinaryOperatorExpression(
                                        new CodeBinaryOperatorExpression(
                                            new CodeBinaryOperatorExpression(
                                                new CodePrimitiveExpression("Errors while trying to create new Rule"),
                                                CodeBinaryOperatorType.Add,
                                                new CodePrimitiveExpression(": ")
                                            ),
                                            CodeBinaryOperatorType.Add,
                                            new CodeVariableReferenceExpression(paramToIdentifier.First().Value)
                                        ),
                                        CodeBinaryOperatorType.Add,
                                        new CodePrimitiveExpression(" ")
                                    ),
                                    CodeBinaryOperatorType.Add,
                                    new CodeVariableReferenceExpression(
                                        "@operator") // @operator is just "operator" in CodeDOM
                                ),
                                new CodePrimitiveExpression(scenarioDefinition.Description),
                                new CodeVariableReferenceExpression(GeneratorConstants.SCENARIO_TAGS_VARIABLE_NAME),
                                new CodeVariableReferenceExpression(GeneratorConstants
                                    .SCENARIO_ARGUMENTS_VARIABLE_NAME),
                                inheritedTagsExpression
                            )
                        )
                    );
                }
                else
                {
                    testMethod.Statements.Add(
                        new CodeVariableDeclarationStatement(
                            _codeDomHelper.GetGlobalizedTypeName(typeof(ScenarioInfo)), // type
                            "scenarioInfo", // variable name
                            new CodeObjectCreateExpression(
                                _codeDomHelper.GetGlobalizedTypeName(typeof(ScenarioInfo)), // constructor type
                                new CodeBinaryOperatorExpression(
                                    new CodeBinaryOperatorExpression(
                                        new CodePrimitiveExpression("Errors while trying to create new Rule"),
                                        CodeBinaryOperatorType.Add,
                                        new CodePrimitiveExpression(": ")
                                    ),
                                    CodeBinaryOperatorType.Add,
                                    new CodeVariableReferenceExpression(paramToIdentifier.First().Value)
                                ),
                                new CodePrimitiveExpression(scenarioDefinition.Description),
                                new CodeVariableReferenceExpression(GeneratorConstants.SCENARIO_TAGS_VARIABLE_NAME),
                                new CodeVariableReferenceExpression(GeneratorConstants.SCENARIO_ARGUMENTS_VARIABLE_NAME),
                                inheritedTagsExpression
                            )
                        )
                    );
                }
            }

            GenerateScenarioInitializeCall(generationContext, scenarioDefinition, testMethod);
            //// NEW CODE START
            if (_setVariantToContextForOutlineTest)
            {
                testMethod.Statements.Add(new CodeMethodInvokeExpression(
                    new CodeMethodReferenceExpression(
                        new CodePropertyReferenceExpression(
                            new CodeFieldReferenceExpression(null, generationContext.TestRunnerField.Name),
                            "ScenarioContext"), "Add", null), new CodeExpression[2]
                    {
                        new CodePrimitiveExpression(_variantHelper.VariantKey),
                        new CodeVariableReferenceExpression(_variantHelper.VariantKey.ToLowerInvariant())
                    }));

                if (!generationContext.GenerateRowTests)
                    testMethod.Parameters.Add(new CodeParameterDeclarationExpression("System.String",
                        _variantHelper.VariantKey.ToLowerInvariant()));
            }
            else if (_setVariantToContextForTest)
            {
                testMethod.Statements.Add(new CodeMethodInvokeExpression(
                    new CodeMethodReferenceExpression(
                        new CodePropertyReferenceExpression(
                            new CodeFieldReferenceExpression(null, generationContext.TestRunnerField.Name),
                            "ScenarioContext"), "Add", null), new CodeExpression[2]
                    {
                        new CodePrimitiveExpression(_variantHelper.VariantKey),
                        new CodePrimitiveExpression(_variantValue)
                    }));
            }

            _setVariantToContextForOutlineTest = false;
            _setVariantToContextForTest = false;
            _variantValue = null;
            //// NEW CODE END


            GenerateTestMethodBody(generationContext, scenarioDefinitionInFeatureFile, testMethod, paramToIdentifier,
                feature);

            GenerateScenarioCleanupMethodCall(generationContext, testMethod, scenarioDefinition);
        }

        internal void GenerateScenarioInitializeCall(TestClassGenerationContext generationContext,
            StepsContainer scenario, CodeMemberMethod testMethod)
        {
            var statements = new List<CodeStatement>();

            using (new SourceLineScope(_reqnrollConfiguration, _codeDomHelper, statements,
                       generationContext.Document.SourceFilePath, scenario.Location))
            {
                statements.Add(new CodeExpressionStatement(
                    new CodeMethodInvokeExpression(
                        new CodeThisReferenceExpression(),
                        generationContext.ScenarioInitializeMethod.Name,
                        new CodeVariableReferenceExpression("scenarioInfo"))));
            }

            testMethod.Statements.AddRange(statements.ToArray());
        }

        internal void GenerateTestMethodBody(TestClassGenerationContext generationContext,
            ScenarioDefinitionInFeatureFile scenarioDefinition, CodeMemberMethod testMethod,
            ParameterSubstitution paramToIdentifier, ReqnrollFeature feature)
        {
            var scenario = scenarioDefinition.Scenario;

            var statementsWhenScenarioIsIgnored = new CodeStatement[]
                { new CodeExpressionStatement(CreateTestRunnerSkipScenarioCall()) };

            var callScenarioStartMethodExpression = new CodeMethodInvokeExpression(new CodeThisReferenceExpression(),
                generationContext.ScenarioStartMethod.Name);

            _codeDomHelper.MarkCodeMethodInvokeExpressionAsAwait(callScenarioStartMethodExpression);

            var statementsWhenScenarioIsExecuted = new List<CodeStatement>
            {
                new CodeExpressionStatement(callScenarioStartMethodExpression)
            };


            if (generationContext.Feature.HasFeatureBackground())
            {
                using (new SourceLineScope(_reqnrollConfiguration, _codeDomHelper, statementsWhenScenarioIsExecuted,
                           generationContext.Document.SourceFilePath, generationContext.Feature.Background.Location))
                {
                    var backgroundMethodCallExpression = new CodeMethodInvokeExpression(
                        new CodeThisReferenceExpression(),
                        generationContext.FeatureBackgroundMethod.Name);

                    _codeDomHelper.MarkCodeMethodInvokeExpressionAsAwait(backgroundMethodCallExpression);
                    statementsWhenScenarioIsExecuted.Add(new CodeExpressionStatement(backgroundMethodCallExpression));
                }
            }

            _scenarioPartHelper.GenerateRuleBackgroundStepsApplicableForThisScenario(generationContext,
                scenarioDefinition, statementsWhenScenarioIsExecuted);

            foreach (var scenarioStep in scenario.Steps)
            {
                _scenarioPartHelper.GenerateStep(generationContext, statementsWhenScenarioIsExecuted, scenarioStep,
                    paramToIdentifier);
            }

            var tagsOfScenarioVariableReferenceExpression =
                new CodeVariableReferenceExpression(GeneratorConstants.SCENARIO_TAGS_VARIABLE_NAME);
            var featureFileTagFieldReferenceExpression =
                new CodeFieldReferenceExpression(null, GeneratorConstants.FEATURE_TAGS_VARIABLE_NAME);

            var scenarioCombinedTagsPropertyExpression =
                new CodePropertyReferenceExpression(new CodeVariableReferenceExpression("scenarioInfo"),
                    "CombinedTags");

            var tagHelperReference =
                new CodeTypeReferenceExpression(_codeDomHelper.GetGlobalizedTypeName(typeof(TagHelper)));
            var scenarioTagIgnoredCheckStatement = new CodeMethodInvokeExpression(tagHelperReference,
                nameof(TagHelper.ContainsIgnoreTag), scenarioCombinedTagsPropertyExpression);
            var featureTagIgnoredCheckStatement = new CodeMethodInvokeExpression(tagHelperReference,
                nameof(TagHelper.ContainsIgnoreTag), featureFileTagFieldReferenceExpression);

            var ifIsIgnoredStatement = new CodeConditionStatement(
                new CodeBinaryOperatorExpression(
                    scenarioTagIgnoredCheckStatement,
                    CodeBinaryOperatorType.BooleanOr,
                    featureTagIgnoredCheckStatement),
                statementsWhenScenarioIsIgnored,
                statementsWhenScenarioIsExecuted.ToArray()
            );

            testMethod.Statements.Add(ifIsIgnoredStatement);
        }

        private CodeMethodInvokeExpression CreateTestRunnerSkipScenarioCall()
        {
            return new CodeMethodInvokeExpression(
                new CodeFieldReferenceExpression(null, "testRunner"),
                nameof(TestRunner.SkipScenario));
        }

        private void AddVariableForTags(CodeMemberMethod testMethod, CodeExpression tagsExpression)
        {
            var tagVariable = new CodeVariableDeclarationStatement(typeof(string[]),
                GeneratorConstants.SCENARIO_TAGS_VARIABLE_NAME, tagsExpression);

            testMethod.Statements.Add(tagVariable);
        }

        private void AddVariableForArguments(CodeMemberMethod testMethod, ParameterSubstitution paramToIdentifier)
        {
            var argumentsExpression = new CodeVariableDeclarationStatement(
                typeof(OrderedDictionary),
                GeneratorConstants.SCENARIO_ARGUMENTS_VARIABLE_NAME,
                new CodeObjectCreateExpression(typeof(OrderedDictionary)));

            testMethod.Statements.Add(argumentsExpression);

            if (paramToIdentifier != null)
            {
                foreach (var parameter in paramToIdentifier)
                {
                    var addArgumentExpression = new CodeMethodInvokeExpression(
                        new CodeMethodReferenceExpression(
                            new CodeTypeReferenceExpression(
                                new CodeTypeReference(GeneratorConstants.SCENARIO_ARGUMENTS_VARIABLE_NAME)),
                            nameof(OrderedDictionary.Add)),
                        new CodePrimitiveExpression(parameter.Key),
                        new CodeVariableReferenceExpression(parameter.Value));

                    testMethod.Statements.Add(addArgumentExpression);
                }
            }
        }

        private void GenerateScenarioCleanupMethodCall(TestClassGenerationContext generationContext,
            CodeMemberMethod testMethod, StepsContainer scenarioDefinition)
        {
            // call scenario cleanup
            if(IsRetryActive && scenarioDefinition.GetTags().Any(c => c.GetNameWithoutAt().Equals("retry", StringComparison.OrdinalIgnoreCase) || 
                                                                      Regex.Match(c.GetNameWithoutAt(), @"^retry(?:\((\d+)\))?$", RegexOptions.IgnoreCase).Success ))
            {
                // Step 1: testRunner.ScenarioContext.TestError != null
                var testErrorNotNullCondition = new CodeBinaryOperatorExpression(
                    new CodePropertyReferenceExpression(
                        new CodePropertyReferenceExpression(
                            new CodeVariableReferenceExpression("testRunner"),
                            "ScenarioContext"),
                        "TestError"),
                    CodeBinaryOperatorType.IdentityInequality,
                    new CodePrimitiveExpression(null)
                );

// Step 2: testRunner.ScenarioContext.TestError.Message
                var testErrorMessageExpression = new CodePropertyReferenceExpression(
                    new CodePropertyReferenceExpression(
                        new CodePropertyReferenceExpression(
                            new CodeVariableReferenceExpression("testRunner"),
                            "ScenarioContext"),
                        "TestError"),
                    "Message"
                );

// Step 3: new AssertionException(testRunner.ScenarioContext.TestError.Message)
                var newAssertionException = new CodeObjectCreateExpression(
                    "NUnit.Framework.AssertionException", // Hardcoded string type name
                    new CodeExpression[] { testErrorMessageExpression }
                );

// Step 4: throw new AssertionException(...)
                var throwStatement = new CodeThrowExceptionStatement(newAssertionException);

// Step 5: if (testRunner.ScenarioContext.TestError != null) { ... }
                var ifStatement = new CodeConditionStatement(
                    testErrorNotNullCondition,
                    new CodeStatement[] { throwStatement }
                );

// Step 6: Add to your method
                testMethod.Statements.Add(ifStatement);
            }
            var expression = new CodeMethodInvokeExpression(
                new CodeThisReferenceExpression(),
                generationContext.ScenarioCleanupMethod.Name);

            _codeDomHelper.MarkCodeMethodInvokeExpressionAsAwait(expression);

            testMethod.Statements.Add(expression);
        }

        private void SetupTestMethod(TestClassGenerationContext generationContext, CodeMemberMethod testMethod,
            StepsContainer scenarioDefinition, IEnumerable<Tag> additionalTags, string variantName,
            string exampleSetIdentifier, bool rowTest = false)
        {
            // ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
            testMethod.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            testMethod.Name = GetTestMethodName(scenarioDefinition, variantName, exampleSetIdentifier);
            var friendlyTestName = scenarioDefinition.Name;
            if (variantName != null)
            {
                friendlyTestName = $"{scenarioDefinition.Name}: {variantName}";
            }

            if (rowTest)
            {
                _testGeneratorProvider.SetRowTest(generationContext, testMethod, friendlyTestName);
            }
            else
            {
                _testGeneratorProvider.SetTestMethod(generationContext, testMethod, friendlyTestName);
            }

            _decoratorRegistry.DecorateTestMethod(generationContext, testMethod,
                ConcatTags(scenarioDefinition.GetTags(), additionalTags), out var scenarioCategories);

            if (scenarioCategories.Any())
            {
                if (IsRetryActive)
                {
                    SetRetry(generationContext, testMethod, scenarioCategories);
                }
                _testGeneratorProvider.SetTestMethodCategories(generationContext, testMethod, scenarioCategories);
            }
        }

        private IEnumerable<Tag> ConcatTags(params IEnumerable<Tag>[] tagLists)
        {
            return tagLists.Where(tagList => tagList != null).SelectMany(tagList => tagList);
        }

        private void GenerateStep(TestClassGenerationContext generationContext, List<CodeStatement> statements,
            Step gherkinStep, ParameterSubstitution paramToIdentifier)
        {
            var testRunnerField = GetTestRunnerExpression();
            var scenarioStep = AsReqnrollStep(gherkinStep);

            //testRunner.Given("something");
            var arguments = new List<CodeExpression>
            {
                GetSubstitutedString(scenarioStep.Text, paramToIdentifier),
                GetDocStringArgExpression(scenarioStep.Argument as DocString, paramToIdentifier),
                GetTableArgExpression(scenarioStep.Argument as Gherkin.Ast.DataTable, statements, paramToIdentifier),
                new CodePrimitiveExpression(scenarioStep.Keyword)
            };

            using (new SourceLineScope(_reqnrollConfiguration, _codeDomHelper, statements,
                       generationContext.Document.SourceFilePath, gherkinStep.Location))
            {
                var expression = new CodeMethodInvokeExpression(
                    testRunnerField,
                    scenarioStep.StepKeyword + "Async",
                    arguments.ToArray());

                _codeDomHelper.MarkCodeMethodInvokeExpressionAsAwait(expression);

                statements.Add(new CodeExpressionStatement(expression));
            }
        }

        private ReqnrollStep AsReqnrollStep(Step step)
        {
            var reqnrollStep = step as ReqnrollStep;
            if (reqnrollStep == null)
            {
                throw new TestGeneratorException("The step must be a ReqnrollStep.");
            }

            return reqnrollStep;
        }

        private CodeExpression GetSubstitutedString(string text, ParameterSubstitution paramToIdentifier)
        {
            if (text == null)
            {
                return new CodeCastExpression(typeof(string), new CodePrimitiveExpression(null));
            }

            if (paramToIdentifier == null)
            {
                return new CodePrimitiveExpression(text);
            }

            var paramRe = new Regex(@"\<(?<param>[^\<\>]+)\>");
            var formatText = text.Replace("{", "{{").Replace("}", "}}");
            var arguments = new List<string>();

            formatText = paramRe.Replace(formatText, match =>
            {
                var param = match.Groups["param"].Value;
                string id;
                if (!paramToIdentifier.TryGetIdentifier(param, out id))
                {
                    return match.Value;
                }

                var argIndex = arguments.IndexOf(id);
                if (argIndex < 0)
                {
                    argIndex = arguments.Count;
                    arguments.Add(id);
                }

                return "{" + argIndex + "}";
            });

            if (arguments.Count == 0)
            {
                return new CodePrimitiveExpression(text);
            }

            var formatArguments = new List<CodeExpression> { new CodePrimitiveExpression(formatText) };
            formatArguments.AddRange(arguments.Select(id => new CodeVariableReferenceExpression(id)));

            return new CodeMethodInvokeExpression(
                new CodeTypeReferenceExpression(typeof(string)),
                "Format",
                formatArguments.ToArray());
        }

        private CodeExpression GetDocStringArgExpression(DocString docString, ParameterSubstitution paramToIdentifier)
        {
            return GetSubstitutedString(docString == null ? null : docString.Content, paramToIdentifier);
        }

        private string GetTestMethodName(StepsContainer scenario, string variantName, string exampleSetIdentifier)
        {
            var methodName = string.Format(GeneratorConstants.TEST_NAME_FORMAT, scenario.Name.ToIdentifier());
            if (variantName == null)
            {
                return methodName;
            }

            var variantNameIdentifier = variantName.ToIdentifier().TrimStart('_');
            methodName = string.IsNullOrEmpty(exampleSetIdentifier)
                ? $"{methodName}__{variantNameIdentifier}"
                : $"{methodName}_{exampleSetIdentifier}__{variantNameIdentifier}";

            return methodName;
        }

        private CodeExpression GetTableArgExpression(Gherkin.Ast.DataTable tableArg, List<CodeStatement> statements,
            ParameterSubstitution paramToIdentifier)
        {
            if (tableArg == null)
            {
                return new CodeCastExpression(_codeDomHelper.GetGlobalizedTypeName(typeof(Table)),
                    new CodePrimitiveExpression(null));
            }

            _tableCounter++;

            //TODO[Gherkin3]: remove dependency on having the first row as header
            var header = tableArg.Rows.First();
            var body = tableArg.Rows.Skip(1).ToArray();

            //Table table0 = new Table(header...);
            var tableVar = new CodeVariableReferenceExpression("table" + _tableCounter);
            statements.Add(
                new CodeVariableDeclarationStatement(_codeDomHelper.GetGlobalizedTypeName(typeof(Table)),
                    tableVar.VariableName,
                    new CodeObjectCreateExpression(
                        _codeDomHelper.GetGlobalizedTypeName(typeof(Table)),
                        GetStringArrayExpression(header.Cells.Select(c => c.Value), paramToIdentifier))));

            foreach (var row in body)
            {
                //table0.AddRow(cells...);
                statements.Add(new CodeExpressionStatement(
                    new CodeMethodInvokeExpression(
                        tableVar,
                        "AddRow",
                        GetStringArrayExpression(row.Cells.Select(c => c.Value), paramToIdentifier))));
            }

            return tableVar;
        }

        private CodeExpression GetStringArrayExpression(IEnumerable<string> items,
            ParameterSubstitution paramToIdentifier)
        {
            return new CodeArrayCreateExpression(typeof(string[]),
                items.Select(item => GetSubstitutedString(item, paramToIdentifier)).ToArray());
        }
        public void SetRetry(TestClassGenerationContext generationContext, CodeMemberMethod testMethod, IEnumerable<string> scenarioCategories)
        {
            var num = 3; 
            var isThereRetryTag = scenarioCategories.Any(c => c.Equals("retry", StringComparison.OrdinalIgnoreCase) || Regex.Match(c, @"^retry(?:\((\d+)\))?$", RegexOptions.IgnoreCase).Success);
            if (isThereRetryTag)
            {
                int? retryValue = scenarioCategories
                    .Select(c =>
                    {
                        if (c.Equals("retry", StringComparison.OrdinalIgnoreCase))
                            return 3;
        
                        var match = Regex.Match(c, @"^retry\((\d+)\)$", RegexOptions.IgnoreCase);
                        return match.Success ? int.Parse(match.Groups[1].Value) : (int?)null;
                    })
                    .FirstOrDefault(v => v.HasValue);
                CodeAttributeDeclaration retryAttribute = new CodeAttributeDeclaration(
                    "NUnit.Framework.Retry",
                    new CodeAttributeArgument(new CodePrimitiveExpression(retryValue))
                );

                testMethod.CustomAttributes.Add(retryAttribute);
            }
            
        }
    }
}