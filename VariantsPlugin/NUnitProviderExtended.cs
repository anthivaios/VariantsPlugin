using System.CodeDom;
using System.Text.RegularExpressions;
using Reqnroll;
using Reqnroll.BoDi;
using Reqnroll.Generator;
using Reqnroll.Generator.CodeDom;
using Reqnroll.Generator.UnitTestProvider;

namespace VariantsPlugin
{

    public class NUnitProviderExtended : NUnit3TestGeneratorProvider , IUnitTestGeneratorProvider
    {
        private readonly CodeDomHelper _codeDomHelper;
        private readonly string _variantKey;
        private IEnumerable<string> _filteredCategories;
        protected internal const string NONPARALLELIZABLE_ATTR = "NUnit.Framework.NonParallelizableAttribute";

        public NUnitProviderExtended(CodeDomHelper codeDomHelper, string variantKey) : base(codeDomHelper)
        {
            _codeDomHelper = codeDomHelper;
            _variantKey = variantKey;
        }
        public UnitTestGeneratorTraits GetTraits()
        {
            return UnitTestGeneratorTraits.RowTests | UnitTestGeneratorTraits.ParallelExecution;
        }

        public void SetTestClassInitializeMethod(TestClassGenerationContext generationContext)
        {
            generationContext.TestClassInitializeMethod.Attributes |= MemberAttributes.Static;
            _codeDomHelper.AddAttribute(generationContext.TestClassInitializeMethod, "NUnit.Framework.OneTimeSetUpAttribute");
        }
        

        public void SetTestClassIgnore(TestClassGenerationContext generationContext)
        {
            _codeDomHelper.AddAttribute(generationContext.TestClass, "NUnit.Framework.IgnoreAttribute", "Ignored feature");
        }

        public void SetTestClassCleanupMethod(TestClassGenerationContext generationContext)
        {
            generationContext.TestClassCleanupMethod.Attributes |= MemberAttributes.Static;
            _codeDomHelper.AddAttribute(generationContext.TestClassCleanupMethod, "NUnit.Framework.OneTimeTearDownAttribute");
        }

        
        // NEW CODE START
        public new void SetRow(TestClassGenerationContext generationContext, CodeMemberMethod testMethod,
            IEnumerable<string> arguments, IEnumerable<string> tags, bool isIgnored)
        {
            
            var args = arguments.Select(
                arg => new CodeAttributeArgument(new CodePrimitiveExpression(arg))).ToList();

            var tagsArray = tags.ToArray();
            var hasVariantTag = tagsArray.Where(t => t.StartsWith($"{_variantKey}:"));

            // addressing ReSharper bug: TestCase attribute with empty string[] param causes inconclusive result - https://youtrack.jetbrains.com/issue/RSRP-279138
            var tagsExceptVariantTags = tagsArray.Where(t => !t.StartsWith($"{_variantKey}:"));
            bool hasExampleTags = tagsExceptVariantTags.Any();
            var exampleTagExpressionList = tagsExceptVariantTags.Select(t => (CodeExpression)new CodePrimitiveExpression(t));
            var exampleTagsExpression = hasExampleTags
                ? new CodeArrayCreateExpression(typeof(string[]), exampleTagExpressionList.ToArray())
                : (CodeExpression) new CodePrimitiveExpression(null);
                
            args.Add(new CodeAttributeArgument(exampleTagsExpression));

            // adds 'Category' named parameter so that NUnit also understands that this test case belongs to the given categories
            if (tagsArray.Any())
            {
                CodeExpression exampleTagsStringExpr = new CodePrimitiveExpression(string.Join(",", tagsArray));
                args.Add(new CodeAttributeArgument("Category", exampleTagsStringExpr));
            }
            
            if (hasVariantTag.Any())
            {
                var example = args[0].Value as CodePrimitiveExpression;
                var testName = $"{testMethod.Name} with {example.Value} and {hasVariantTag.ToList().First()}";
                args.Add(new CodeAttributeArgument("TestName", new CodePrimitiveExpression(testName)));
            }
            

            if (isIgnored)
                args.Add(new CodeAttributeArgument("IgnoreReason", new CodePrimitiveExpression("Ignored by @ignore tag")));

            CodeDomHelper.AddAttribute(testMethod, ROW_ATTR, args.ToArray());
        }
        // NEW CODE END
        

        public override void SetTestMethod(TestClassGenerationContext generationContext, CodeMemberMethod testMethod, string friendlyTestName)
        {
            int lastUnderscoreIndex = friendlyTestName.LastIndexOf("__", StringComparison.Ordinal);

            if (lastUnderscoreIndex != -1)
            {
                string beforeLast = friendlyTestName.Substring(0, lastUnderscoreIndex);
                string afterLast = friendlyTestName.Substring(lastUnderscoreIndex + 2);
                friendlyTestName = beforeLast + afterLast;
            }

            base.SetTestMethod(generationContext, testMethod, friendlyTestName);
        }
        
        public override void SetTestMethodCategories(TestClassGenerationContext generationContext, CodeMemberMethod testMethod, IEnumerable<string> scenarioCategories)
        {
            // Remove categories that are not the current variant
            var variantValue = testMethod.Name.Split(new []{"__"}, StringSplitOptions.None).Last();
            _filteredCategories = scenarioCategories.Where(a => !a.StartsWith(_variantKey) || a.ToLower().Equals($"{_variantKey.ToLower()}:{variantValue.ToLower()}"));
            
            base.SetTestMethodCategories(generationContext, testMethod, _filteredCategories);
        }

        

        public void SetTestMethodIgnore(TestClassGenerationContext generationContext, CodeMemberMethod testMethod)
        {
            _codeDomHelper.AddAttribute(testMethod, "NUnit.Framework.IgnoreAttribute", "Ignored scenario");
        }
        
    }
}