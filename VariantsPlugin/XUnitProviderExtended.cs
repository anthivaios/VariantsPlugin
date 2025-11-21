using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using Reqnroll.Generator.CodeDom;
using Reqnroll.BoDi;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;
using Reqnroll;
using Reqnroll.Generator;
using Reqnroll.Generator.UnitTestProvider;

namespace VariantsPlugin
{
    public class XUnitProviderExtended : XUnit2TestGeneratorProvider
    {
        private CodeTypeDeclaration _currentFixtureDataTypeDeclaration = null;
        private readonly CodeTypeReference _objectCodeTypeReference = new(typeof(object));
        protected internal const string THEORY_ATTRIBUTE = "Xunit.SkippableTheoryAttribute";
        protected internal const string INLINEDATA_ATTRIBUTE = "Xunit.InlineDataAttribute";
        protected internal const string ICLASSFIXTURE_INTERFACE = "Xunit.IClassFixture";
        protected internal const string COLLECTION_ATTRIBUTE = "Xunit.CollectionAttribute";
        protected internal const string OUTPUT_INTERFACE = "Xunit.Abstractions.ITestOutputHelper";
        protected internal const string OUTPUT_INTERFACE_PARAMETER_NAME = "testOutputHelper";
        protected internal const string OUTPUT_INTERFACE_FIELD_NAME = "_testOutputHelper";
        protected internal const string FIXTUREDATA_PARAMETER_NAME = "fixtureData";
        protected internal const string COLLECTION_DEF = "Xunit.Collection";
        protected internal const string COLLECTION_TAG = "xunit:collection";
        protected internal const string FEATURE_TITLE_PROPERTY_NAME = "FeatureTitle";
        protected internal const string DESCRIPTION_PROPERTY_NAME = "Description";
        protected internal const string FACT_ATTRIBUTE = "Xunit.SkippableFactAttribute";
        protected internal const string FACT_ATTRIBUTE_SKIP_PROPERTY_NAME = "Skip";
        protected internal const string THEORY_ATTRIBUTE_SKIP_PROPERTY_NAME = "Skip";
        protected internal const string SKIP_REASON = "Ignored";
        protected internal const string TRAIT_ATTRIBUTE = "Xunit.TraitAttribute";
        protected internal const string CATEGORY_PROPERTY_NAME = "Category";
        protected internal const string IGNORE_TEST_CLASS = "IgnoreTestClass";
        protected internal const string NONPARALLELIZABLE_COLLECTION_NAME = "ReqnrollNonParallelizableFeatures";
        protected internal const string IASYNCLIFETIME_INTERFACE = "Xunit.IAsyncLifetime";
        private readonly CodeDomHelper _codeDomHelper;
        private readonly string _variantKey;
        private IEnumerable<string> _filteredCategories;

        public XUnitProviderExtended(CodeDomHelper codeDomHelper, string variantKey) : base(codeDomHelper)
        {
            _codeDomHelper = codeDomHelper;
            _variantKey = variantKey;
        }

        public override void SetRow(TestClassGenerationContext generationContext, CodeMemberMethod testMethod, IEnumerable<string> arguments, IEnumerable<string> tags, bool isIgnored)
        {
            //TODO: better handle "ignored"
            if (isIgnored)
            {
                return;
            }

            var args = arguments.Select(arg => new CodeAttributeArgument(new CodePrimitiveExpression(arg))).ToList();
            var tagsWithoutVariantTags = tags.Where(t=> !t.StartsWith(_variantKey));
            args.Add(
                new CodeAttributeArgument(
                    new CodeArrayCreateExpression(typeof(string[]), tagsWithoutVariantTags.Select(t => (CodeExpression)new CodePrimitiveExpression(t)).ToArray())));

            _codeDomHelper.AddAttribute(testMethod, INLINEDATA_ATTRIBUTE, args.ToArray());
        }

        public override void SetTestMethod(TestClassGenerationContext generationContext, CodeMemberMethod testMethod, string friendlyTestName)
        {
            friendlyTestName = friendlyTestName.Replace("__", "");
            base.SetTestMethod(generationContext, testMethod, friendlyTestName);
        }

        public override void SetTestMethodCategories(TestClassGenerationContext generationContext, CodeMemberMethod testMethod, IEnumerable<string> scenarioCategories)
        {
            var variantValue = testMethod.Name.Split(new []{"__"}, StringSplitOptions.None).Last();
            var filteredCategories = scenarioCategories.Where(a => !a.StartsWith(_variantKey) || a.ToLower().Equals($"{_variantKey.ToLower()}:{variantValue.ToLower()}"));
            base.SetTestMethodCategories(generationContext, testMethod, filteredCategories);
        }
        
    }
}