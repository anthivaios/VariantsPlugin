using System.CodeDom;
using System.Collections.Specialized;
using System.Reflection;
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
        private readonly RetryHelper _retryHelper;
        private List<Tag> _featureVariantTags;
        private bool _setVariantToContextForOutlineTest;
        private bool _setVariantToContextForTest;
        private string _variantValue;
        readonly ScenarioPartHelper _scenarioPartHelper;
        public const string CustomGeneratedComment = "Generation customised by VariantPlugin";
        private const string IGNORE_TAG = "@Ignore";

        public bool IsRetryActive;
        // STARTS CODE

        public FeatureGeneratorExtended(IUnitTestGeneratorProvider testGeneratorProvider, CodeDomHelper codeDomHelper,
            ReqnrollConfiguration reqnrollConfiguration, IDecoratorRegistry decoratorRegistry, string variantKey,
            bool isRetryActive)
            : base(decoratorRegistry, testGeneratorProvider, codeDomHelper, reqnrollConfiguration)
        {
            _testGeneratorProvider = testGeneratorProvider;
            _codeDomHelper = codeDomHelper;
            _reqnrollConfiguration = reqnrollConfiguration;
            _decoratorRegistry = decoratorRegistry;
            _scenarioPartHelper = new ScenarioPartHelper(_reqnrollConfiguration, _codeDomHelper);
            _variantHelper = new VariantHelper(variantKey); //NEW CODE
            _retryHelper = new RetryHelper();
            IsRetryActive = isRetryActive;
        }

        // ENDS CODE
        public string TestClassNameFormat { get; set; } = "{0}Feature";

        public UnitTestFeatureGenerationResult GenerateUnitTestFixture(ReqnrollDocument document, string testClassName,
            string targetNamespace)
        {
            var codeNamespace = CreateNamespace(targetNamespace);
            var feature = document.ReqnrollFeature;

            testClassName ??= string.Format(TestClassNameFormat, feature.Name.ToIdentifier());
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


            // STARTS CODE
            var variantTags = _variantHelper.GetFeatureVariantTagValues(feature);
            _featureVariantTags = _variantHelper.FeatureTags(feature);


            if (IsRetryActive)
            {
                var retryTag = _retryHelper.GetRetryTag(feature);
                if (retryTag.Count > 1)
                    throw new TestGeneratorException(
                        $"Multiple Feature Retry tags on Feature: {feature.Name}");
                int featureRetries = retryTag == null || retryTag.All(string.IsNullOrEmpty)
                    ? 0
                    : _retryHelper.GetRetriesNumber(retryTag);
                _retryHelper.SetFeatureRetriesNumber(featureRetries);

                if (_retryHelper.AnyScenarioHasRetryTag(feature) && _retryHelper.FeatureHasRetryTag)
                    throw new TestGeneratorException(
                        "Retry tags were detected at feature and scenario level, please specify at one level or the other.");
            }


            if (_variantHelper.AnyScenarioHasVariantTag(feature) && _variantHelper.FeatureHasVariantTags)
                throw new TestGeneratorException(
                    "Variant tags were detected at feature and scenario level, please specify at one level or the other.");
            //NEW CODE END
            var pickleIndex = 0;
            foreach (var scenarioDefinition in GetScenarioDefinitions(feature))
            {
                if (string.IsNullOrEmpty(scenarioDefinition.Scenario.Name))
                    throw new TestGeneratorException("The scenario must have a title specified.");

                if (scenarioDefinition.IsScenarioOutline)
                {
                    //NEW CODE START
                    variantTags = _variantHelper.FeatureHasVariantTags
                        ? variantTags
                        : _variantHelper.GetScenarioVariantTagValues(scenarioDefinition.ScenarioDefinition);
                    GenerateScenarioOutlineTest(generationContext, scenarioDefinition, ref pickleIndex, variantTags);
                }
                else
                {
                    variantTags = _variantHelper.FeatureHasVariantTags
                        ? variantTags
                        : _variantHelper.GetScenarioVariantTagValues(scenarioDefinition.ScenarioDefinition);
                    if (variantTags.Count > 0)
                    {
                        variantTags.ForEach(a =>
                            GenerateTest(generationContext, (ScenarioDefinitionInFeatureFile)scenarioDefinition, pickleIndex, a));
                    }
                    else
                    {
                        GenerateTest(generationContext, (ScenarioDefinitionInFeatureFile)scenarioDefinition, pickleIndex, null);
                    }
                    pickleIndex++;
                }
            }

            // ENDS CODE
            _testGeneratorProvider.FinalizeTestClass(generationContext);
            codeNamespace.Comments.Add(new CodeCommentStatement(new CodeComment(CustomGeneratedComment))); //NEW CODE
            var type = generationContext.GetType();
            var prop = type.GetProperty("FeatureMessages", BindingFlags.Instance | BindingFlags.NonPublic);
            var valueFeatureMessages = prop.GetValue(generationContext) as string;
            return new UnitTestFeatureGenerationResult(codeNamespace, valueFeatureMessages,
                generationContext.FeatureMessagesResourceName, generationContext.GenerationWarnings);
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
            scenarioCleanupMethod.Name = GeneratorConstants.SCENARIO_CLEANUP_NAME;

            _codeDomHelper.MarkCodeMemberMethodAsAsync(scenarioCleanupMethod);

            // call collect errors
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
            var scenarioInitializeMethod = generationContext.ScenarioInitializeMethod;

            scenarioInitializeMethod.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            scenarioInitializeMethod.Name = GeneratorConstants.SCENARIO_INITIALIZE_NAME;
            scenarioInitializeMethod.Parameters.Add(
                new CodeParameterDeclarationExpression(
                    new CodeTypeReference(typeof(ScenarioInfo), CodeTypeReferenceOptions.GlobalReference),
                    "scenarioInfo"));
            scenarioInitializeMethod.Parameters.Add(
                new CodeParameterDeclarationExpression(
                    new CodeTypeReference(typeof(RuleInfo), CodeTypeReferenceOptions.GlobalReference), "ruleInfo"));

            //testRunner.OnScenarioInitialize(scenarioInfo, ruleInfo);
            var testRunnerField = _scenarioPartHelper.GetTestRunnerExpression();
            scenarioInitializeMethod.Statements.Add(
                new CodeMethodInvokeExpression(
                    testRunnerField,
                    nameof(ITestRunner.OnScenarioInitialize),
                    new CodeVariableReferenceExpression("scenarioInfo"),
                    new CodeVariableReferenceExpression("ruleInfo")));
        }

        private void GenerateScenarioOutlineTest(TestClassGenerationContext generationContext,
            ScenarioDefinitionInFeatureFile scenarioDefinitionInFeatureFile, ref int pickleIndex, List<string> variantTags = null)
        {
            var scenarioOutline = scenarioDefinitionInFeatureFile.ScenarioOutline;
            ValidateExampleSetConsistency(scenarioOutline);

            var paramToIdentifier = CreateParamToIdentifierMapping(scenarioOutline);

            var scenarioOutlineTestMethod = CreateScenarioOutlineTestMethod(generationContext, scenarioOutline, paramToIdentifier);
            var exampleTagsParam = new CodeVariableReferenceExpression(GeneratorConstants.SCENARIO_OUTLINE_EXAMPLE_TAGS_PARAMETER);

            //NEW CODE START
            if (generationContext.GenerateRowTests)
            {
                if (variantTags?.Count > 0)
                    GenerateScenarioOutlineExamplesAsRowTests(generationContext, scenarioOutline,
                        scenarioOutlineTestMethod, ref pickleIndex, variantTags);
                else
                    GenerateScenarioOutlineExamplesAsRowTests(generationContext, scenarioOutline,
                        scenarioOutlineTestMethod, ref pickleIndex, null);
            }
            else
            {
                if (variantTags?.Count > 0)
                {
                    foreach (var variant in variantTags)
                    {
                        GenerateScenarioOutlineExamplesAsIndividualMethods(scenarioOutline,
                            generationContext, scenarioOutlineTestMethod, paramToIdentifier, ref pickleIndex, variant);
                    }
                }
                else
                    GenerateScenarioOutlineExamplesAsIndividualMethods(scenarioOutline, generationContext,
                        scenarioOutlineTestMethod, paramToIdentifier, ref pickleIndex, null);
            }
            //NEW CODE END

            GenerateTestBody(generationContext, scenarioDefinitionInFeatureFile, scenarioOutlineTestMethod,
                exampleTagsParam, paramToIdentifier,true);
        }

        private ParameterSubstitution CreateParamToIdentifierMapping(ScenarioOutline scenarioOutline)
        {
            var paramToIdentifier = new ParameterSubstitution();
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
                    paramToIdentifier[i] = new KeyValuePair<string, string>(paramToIdentifier[i].Key, paramToIdentifier[i].Value + suffix);
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
            TestClassGenerationContext generationContext, CodeMemberMethod scenarioOutlineTestMethod,
            ParameterSubstitution paramToIdentifier, ref int pickleIndex, string tag = null)
        {
            var exampleSetIndex = 0;

            foreach (var exampleSet in scenarioOutline.Examples)
            {
                var useFirstColumnAsName = CanUseFirstColumnAsName(exampleSet.TableBody);
                string exampleSetIdentifier;

                if (string.IsNullOrEmpty(exampleSet.Name))
                {
                    if (scenarioOutline.Examples.Count(es => string.IsNullOrEmpty(es.Name)) > 1)
                    {
                        exampleSetIdentifier = $"ExampleSet {exampleSetIndex}".ToIdentifier();
                    }
                    else
                    {
                        exampleSetIdentifier = null;
                    }
                }
                else
                {
                    exampleSetIdentifier = exampleSet.Name.ToIdentifier();
                }


                foreach (var example in exampleSet.TableBody.Select((r, i) => new { Row = r, Index = i }))
                {
                    var variantName = useFirstColumnAsName ? example.Row.Cells.First().Value : $"Variant {example.Index}";
                    GenerateScenarioOutlineTestVariant(generationContext, scenarioOutline, scenarioOutlineTestMethod, paramToIdentifier, exampleSet.Name ?? "", exampleSetIdentifier, example.Row, pickleIndex, exampleSet.Tags.ToArray(), variantName, tag);
                    pickleIndex++;
                }

                exampleSetIndex++;
            }
        }
        private bool CanUseFirstColumnAsName(IEnumerable<TableRow> tableBody)
        {
            var tableBodyArray = tableBody.ToArray();
            if (tableBodyArray.Any(r => !r.Cells.Any()))
            {
                return false;
            }

            return tableBodyArray.Select(r => r.Cells.First().Value.ToIdentifier()).Distinct().Count() == tableBodyArray.Length;
        }

        private void GenerateScenarioOutlineExamplesAsRowTests(TestClassGenerationContext generationContext,
            ScenarioOutline scenarioOutline, CodeMemberMethod scenarioOutlineTestMethod, ref int pickleIndex,
            List<string> variantTags = null)
        {
            SetupTestMethod(generationContext, scenarioOutlineTestMethod, scenarioOutline, null, null, null, null,true);
            foreach (var example in scenarioOutline.Examples)
            {
                //NEW CODE START
                var hasVariantTags = variantTags?.Count > 0;
                if (hasVariantTags)
                {
                    scenarioOutlineTestMethod.Parameters.RemoveAt(scenarioOutlineTestMethod.Parameters.Count - 1);
                    scenarioOutlineTestMethod.Parameters.Add(new CodeParameterDeclarationExpression(typeof(string),
                        _variantHelper.VariantKey.ToLowerInvariant()));
                    scenarioOutlineTestMethod.Parameters.Add(
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
                            var arguments = tableRow.Cells.Select(c => c.Value).Concat([pickleIndex.ToString()]).ToList();
                            arguments.Add($"{_variantHelper.VariantKey}:{variant}");
                            _testGeneratorProvider.SetRow(generationContext, scenarioOutlineTestMethod,
                                exampleList.Concat(arguments).ToList(),
                                GetNonIgnoreTags(example.Tags), HasIgnoreTag(example.Tags));
                        }
                    }
                    else
                    {
                        var arguments = tableRow.Cells.Select(c => c.Value).Concat([pickleIndex.ToString()]).ToList();
                        exampleList.AddRange(arguments);
                        _testGeneratorProvider.SetRow(generationContext, scenarioOutlineTestMethod, exampleList,
                            GetNonIgnoreTags(example.Tags), HasIgnoreTag(example.Tags));
                    }
                    pickleIndex++;
                    //NEW CODE END
                }
            }
        }
        private IEnumerable<string> GetNonIgnoreTags(IEnumerable<Tag> tags)
        {
            return tags.Where(t => !t.Name.Equals(IGNORE_TAG, StringComparison.InvariantCultureIgnoreCase)).Select(t => t.GetNameWithoutAt());
        }
        private bool HasIgnoreTag(IEnumerable<Tag> tags)
        {
            return tags.Any(t => t.Name.Equals(IGNORE_TAG, StringComparison.InvariantCultureIgnoreCase));
        }

        private CodeMemberMethod CreateScenarioOutlineTestMethod(TestClassGenerationContext generationContext,
            ScenarioOutline scenarioOutline, ParameterSubstitution paramToIdentifier)
        {
            var testMethod = _codeDomHelper.CreateMethod(generationContext.TestClass);

            testMethod.Attributes = MemberAttributes.Public;
            testMethod.Name = string.Format(GeneratorConstants.TEST_NAME_FORMAT, scenarioOutline.Name.ToIdentifier());

            _codeDomHelper.MarkCodeMemberMethodAsAsync(testMethod);

            foreach (var pair in paramToIdentifier)
            {
                testMethod.Parameters.Add(new CodeParameterDeclarationExpression(typeof(string), pair.Value));
            }
            testMethod.Parameters.Add(new CodeParameterDeclarationExpression(typeof(string), GeneratorConstants.PICKLEINDEX_PARAMETER_NAME));
            testMethod.Parameters.Add(new CodeParameterDeclarationExpression(typeof(string[]), GeneratorConstants.SCENARIO_OUTLINE_EXAMPLE_TAGS_PARAMETER));
            return testMethod;
        }

        private void GenerateScenarioOutlineTestVariant(TestClassGenerationContext generationContext,
            ScenarioOutline scenarioOutline, CodeMemberMethod scenarioOutlineTestMethod,
            IEnumerable<KeyValuePair<string, string>> paramToIdentifier, string exampleSetTitle,
            string exampleSetIdentifier, Gherkin.Ast.TableRow row, int pickleIndex, IEnumerable<Tag> exampleSetTags, string variantName,
            string tag = null)
        {
            // START NEW CODE
            variantName = string.IsNullOrEmpty(tag) ? variantName : $"{variantName}_{tag}";
            //END NEW CODE
            var testMethod = CreateTestMethod(generationContext, scenarioOutline, exampleSetTags, variantName,
                exampleSetIdentifier);
            
            //call test implementation with the params
            var argumentExpressions = row.Cells.Select(paramCell => new CodePrimitiveExpression(paramCell.Value)).Cast<CodeExpression>().ToList();
            argumentExpressions.Add(new CodePrimitiveExpression(pickleIndex.ToString()));
            argumentExpressions.Add(_scenarioPartHelper.GetStringArrayExpression(exampleSetTags));

            //// NEW CODE START
            if (tag != null)
            {
                var s = new CodePrimitiveExpression(tag);
                argumentExpressions.Add(s);
                _setVariantToContextForOutlineTest = true;
            }
            //// NEW CODE END

            var statements = new List<CodeStatement>();

            using (new SourceLineScope(_reqnrollConfiguration, _codeDomHelper, statements, generationContext.Document.SourceFilePath, scenarioOutline.Location))
            {
                var callTestMethodExpression = new CodeMethodInvokeExpression(
                    new CodeThisReferenceExpression(),
                    scenarioOutlineTestMethod.Name,
                    argumentExpressions.ToArray());

                _codeDomHelper.MarkCodeMethodInvokeExpressionAsAwait(callTestMethodExpression);

                statements.Add(new CodeExpressionStatement(callTestMethodExpression));
            }

            testMethod.Statements.AddRange(statements.ToArray());

            //_linePragmaHandler.AddLineDirectiveHidden(testMethod.Statements);
            var arguments = paramToIdentifier.Select((pToId, paramIndex) => new KeyValuePair<string, string>(pToId.Key, row.Cells.ElementAt(paramIndex).Value)).ToList();

            // Use the identifier of the example set (e.g. ExampleSet0, ExampleSet1) if we have it.
            // Otherwise, use the title of the example set provided by the user in the feature file.
            string exampleSetName = string.IsNullOrEmpty(exampleSetIdentifier) ? exampleSetTitle : exampleSetIdentifier;
            _testGeneratorProvider.SetTestMethodAsRow(generationContext, testMethod, scenarioOutline.Name, exampleSetName, variantName, arguments);
        }

        private CodeMemberMethod CreateTestMethod(TestClassGenerationContext generationContext, StepsContainer scenario,
            IEnumerable<Tag> additionalTags, string variantName = null, string exampleSetIdentifier = null, string tag = null)
        {
            var method = generationContext.TestClass.CreateMethod();
            _codeDomHelper.MarkCodeMemberMethodAsAsync(method);
            SetupTestMethod(generationContext, method, scenario, additionalTags, variantName, exampleSetIdentifier,
                tag, false);
            return method;
        }

        private void GenerateTest(TestClassGenerationContext generationContext,
            ScenarioDefinitionInFeatureFile scenarioDefinitionInFeatureFile, int pickleIndex, string tag = null)
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
                null, null, null, variantName);
            GenerateTestBody(generationContext, scenarioDefinitionInFeatureFile, testMethod, pickleIndex: pickleIndex);
        }

        private void GenerateTestBody(TestClassGenerationContext generationContext,
            ScenarioDefinitionInFeatureFile scenarioDefinitionInFeatureFile, CodeMemberMethod testMethod,
            CodeExpression additionalTagsExpression = null, ParameterSubstitution paramToIdentifier = null,
            bool pickleIdIncludedInParameters = false, int? pickleIndex = null)
        {
            if (!pickleIdIncludedInParameters && pickleIndex == null)
                throw new ArgumentNullException(nameof(pickleIndex));
            var scenarioDefinition = scenarioDefinitionInFeatureFile.ScenarioDefinition;
            var feature = scenarioDefinitionInFeatureFile.Feature;

            //call test setup
            //ScenarioInfo scenarioInfo = new ScenarioInfo("xxxx", tags...);
            CodeExpression inheritedTagsExpression;
            var featureTagsExpression =
                new CodeFieldReferenceExpression(null, GeneratorConstants.FEATURE_TAGS_VARIABLE_NAME);
            var ruleTagsExpression =
                _scenarioPartHelper.GetStringArrayExpression(scenarioDefinitionInFeatureFile.Rule?.Tags ?? []);
            if (scenarioDefinitionInFeatureFile.Rule != null && scenarioDefinitionInFeatureFile.Rule.Tags.Any())
            {
                var tagHelperReference =
                    new CodeTypeReferenceExpression(new CodeTypeReference(typeof(TagHelper),
                        CodeTypeReferenceOptions.GlobalReference));
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

            // Cucumber Messages support uses a new variables: pickleIndex
            // The pickleIndex tells the runtime which Pickle this test corresponds to. 
            // When Backgrounds and Rule Backgrounds are used, we don't know ahead of time how many Steps there are in the Pickle.
            AddVariableForPickleIndex(testMethod, pickleIdIncludedInParameters, pickleIndex);

            var scenarioName = scenarioDefinition.Name;
            if (_variantValue != null)
            {
                scenarioName = scenarioName + $": {_variantValue}";
            }

            //// NEW CODE START
            if (paramToIdentifier == null)
            {
                testMethod.Statements.Add(
                    new CodeVariableDeclarationStatement(
                        new CodeTypeReference(typeof(ScenarioInfo), CodeTypeReferenceOptions.GlobalReference),
                        "scenarioInfo",
                        new CodeObjectCreateExpression(
                            new CodeTypeReference(typeof(ScenarioInfo), CodeTypeReferenceOptions.GlobalReference),
                            new CodePrimitiveExpression(scenarioName),
                            new CodePrimitiveExpression(scenarioDefinition.Description),
                            new CodeVariableReferenceExpression(GeneratorConstants.SCENARIO_TAGS_VARIABLE_NAME),
                            new CodeVariableReferenceExpression(GeneratorConstants.SCENARIO_ARGUMENTS_VARIABLE_NAME),
                            inheritedTagsExpression,
                            new CodeVariableReferenceExpression(GeneratorConstants.PICKLEINDEX_VARIABLE_NAME))));
            }
            else
            {
                var hasOperatorVariable = scenarioDefinition.GetTags()
                    .Any(c => c.GetNameWithoutAt().StartsWith($"{_variantHelper.VariantKey}:"));


                if (hasOperatorVariable)
                {
                    testMethod.Statements.Add(
                        new CodeVariableDeclarationStatement(
                            new CodeTypeReference(typeof(ScenarioInfo), CodeTypeReferenceOptions.GlobalReference),
                            "scenarioInfo",
                            new CodeObjectCreateExpression(
                                new CodeTypeReference(typeof(ScenarioInfo), CodeTypeReferenceOptions.GlobalReference),
                                new CodeBinaryOperatorExpression(
                                    new CodeBinaryOperatorExpression(
                                        new CodeBinaryOperatorExpression(
                                            new CodeBinaryOperatorExpression(
                                                new CodePrimitiveExpression(scenarioDefinition.Name),
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
                                inheritedTagsExpression,
                                new CodeVariableReferenceExpression(GeneratorConstants.PICKLEINDEX_VARIABLE_NAME))));
                }
                else
                {
                    testMethod.Statements.Add(
                        new CodeVariableDeclarationStatement(
                            new CodeTypeReference(typeof(ScenarioInfo), CodeTypeReferenceOptions.GlobalReference),
                            "scenarioInfo",
                            new CodeObjectCreateExpression(
                                new CodeTypeReference(typeof(ScenarioInfo), CodeTypeReferenceOptions.GlobalReference),
                                new CodeBinaryOperatorExpression(
                                    new CodeBinaryOperatorExpression(
                                        new CodePrimitiveExpression(scenarioDefinition.Name),
                                        CodeBinaryOperatorType.Add,
                                        new CodePrimitiveExpression(": ")
                                    ),
                                    CodeBinaryOperatorType.Add,
                                    new CodeVariableReferenceExpression(paramToIdentifier.First().Value)
                                ),
                                new CodePrimitiveExpression(scenarioDefinition.Description),
                                new CodeVariableReferenceExpression(GeneratorConstants.SCENARIO_TAGS_VARIABLE_NAME),
                                new CodeVariableReferenceExpression(GeneratorConstants
                                    .SCENARIO_ARGUMENTS_VARIABLE_NAME),
                                inheritedTagsExpression,
                                new CodeVariableReferenceExpression(GeneratorConstants.PICKLEINDEX_VARIABLE_NAME))));
                }
            }

            //// NEW CODE END

            AddVariableForRuleTags(testMethod, ruleTagsExpression);

            testMethod.Statements.Add(
                new CodeVariableDeclarationStatement(
                    new CodeTypeReference(typeof(RuleInfo), CodeTypeReferenceOptions.GlobalReference), "ruleInfo",
                    scenarioDefinitionInFeatureFile.Rule == null
                        ? new CodePrimitiveExpression(null)
                        : new CodeObjectCreateExpression(
                            new CodeTypeReference(typeof(RuleInfo), CodeTypeReferenceOptions.GlobalReference),
                            new CodePrimitiveExpression(scenarioDefinitionInFeatureFile.Rule.Name),
                            new CodePrimitiveExpression(scenarioDefinitionInFeatureFile.Rule.Description),
                            new CodeVariableReferenceExpression(GeneratorConstants.RULE_TAGS_VARIABLE_NAME))
                ));

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

        private void AddVariableForRuleTags(CodeMemberMethod testMethod, CodeExpression tagsExpression)
        {
            var tagVariable = new CodeVariableDeclarationStatement(typeof(string[]),
                GeneratorConstants.RULE_TAGS_VARIABLE_NAME, tagsExpression);

            testMethod.Statements.Add(tagVariable);
        }

        public void AddVariableForPickleIndex(CodeMemberMethod testMethod, bool pickleIdIncludedInParameters,
            int? pickleIndex)
        {
            _scenarioPartHelper.AddVariableForPickleIndex(testMethod, pickleIdIncludedInParameters, pickleIndex);
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
                        new CodeVariableReferenceExpression("scenarioInfo"),
                        new CodeVariableReferenceExpression("ruleInfo"))));
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

            var featureFileTagFieldReferenceExpression =
                new CodeFieldReferenceExpression(null, GeneratorConstants.FEATURE_TAGS_VARIABLE_NAME);

            var scenarioCombinedTagsPropertyExpression =
                new CodePropertyReferenceExpression(new CodeVariableReferenceExpression("scenarioInfo"),
                    "CombinedTags");

            var tagHelperReference =
                new CodeTypeReferenceExpression(new CodeTypeReference(typeof(TagHelper),
                    CodeTypeReferenceOptions.GlobalReference));
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
            var testRunnerField = _scenarioPartHelper.GetTestRunnerExpression();
            var callSkipScenarioExpression = new CodeMethodInvokeExpression(
                testRunnerField,
                nameof(TestRunner.SkipScenarioAsync));
            _codeDomHelper.MarkCodeMethodInvokeExpressionAsAwait(callSkipScenarioExpression);

            return callSkipScenarioExpression;
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
                new CodeTypeReference(typeof(OrderedDictionary), CodeTypeReferenceOptions.GlobalReference),
                GeneratorConstants.SCENARIO_ARGUMENTS_VARIABLE_NAME,
                new CodeObjectCreateExpression(new CodeTypeReference(typeof(OrderedDictionary),
                    CodeTypeReferenceOptions.GlobalReference)));

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
            // START NEW CODE
            // call scenario cleanup
            if (IsRetryActive && (scenarioDefinition.GetTags()
                                      .Any(c =>
                                          c.GetNameWithoutAt().Equals("retry", StringComparison.OrdinalIgnoreCase) ||
                                          Regex.Match(c.GetNameWithoutAt(), @"^retry(?:\((\d+)\))?$",
                                              RegexOptions.IgnoreCase).Success)
                                  || _retryHelper.FeatureHasRetryTag))
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

            // END NEW CODE
            var expression = new CodeMethodInvokeExpression(
                new CodeThisReferenceExpression(),
                generationContext.ScenarioCleanupMethod.Name);

            _codeDomHelper.MarkCodeMethodInvokeExpressionAsAwait(expression);

            testMethod.Statements.Add(expression);
        }

        private void SetupTestMethod(TestClassGenerationContext generationContext, CodeMemberMethod testMethod,
            StepsContainer scenarioDefinition, IEnumerable<Tag> additionalTags, string variantName,
            string exampleSetIdentifier, string tag, bool rowTest = false)
        {
            // ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
            testMethod.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            testMethod.Name = GetTestMethodName(scenarioDefinition, tag, exampleSetIdentifier);
            // NEW CODE START
            var friendlyTestName = scenarioDefinition.Name;
            if (tag != null)
            {
                friendlyTestName = $"{scenarioDefinition.Name}: {tag}";
            }
            // NEW CODE END
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
                return new CodeCastExpression(
                    new CodeTypeReference(typeof(Table), CodeTypeReferenceOptions.GlobalReference),
                    new CodePrimitiveExpression(null));
            }

            _tableCounter++;

            //TODO[Gherkin3]: remove dependency on having the first row as header
            var header = tableArg.Rows.First();
            var body = tableArg.Rows.Skip(1).ToArray();

            //Table table0 = new Table(header...);
            var tableVar = new CodeVariableReferenceExpression("table" + _tableCounter);
            statements.Add(
                new CodeVariableDeclarationStatement(
                    new CodeTypeReference(typeof(Table), CodeTypeReferenceOptions.GlobalReference),
                    tableVar.VariableName,
                    new CodeObjectCreateExpression(
                        new CodeTypeReference(typeof(Table), CodeTypeReferenceOptions.GlobalReference),
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

        // START CODE
        public void SetRetry(TestClassGenerationContext generationContext, CodeMemberMethod testMethod,
            IEnumerable<string> scenarioCategories)
        {
            if (scenarioCategories == null) return;

            // Try to extract a retry count from scenario tags
            int? retryValue = scenarioCategories
                .Select(c =>
                {
                    if (c.Equals("retry", StringComparison.OrdinalIgnoreCase))
                        return 3;

                    var match = Regex.Match(c, @"^retry\((\d+)\)$", RegexOptions.IgnoreCase);
                    return match.Success ? int.Parse(match.Groups[1].Value) : (int?)null;
                })
                .FirstOrDefault(v => v.HasValue);

            // Fallback to feature-level retry count if applicable
            if (_retryHelper.FeatureHasRetryTag && _retryHelper.FeatureRetryCount > 0)
            {
                retryValue = _retryHelper.FeatureRetryCount;
            }

            if (retryValue.HasValue)
            {
                var retryAttribute = new CodeAttributeDeclaration(
                    "NUnit.Framework.Retry",
                    new CodeAttributeArgument(new CodePrimitiveExpression(retryValue.Value))
                );

                testMethod.CustomAttributes.Add(retryAttribute);
            }
        }
        // END CODE
    }
}