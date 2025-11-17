using System.CodeDom;
using System.Text.RegularExpressions;
using Reqnroll;
using Reqnroll.BoDi;
using Reqnroll.Generator;
using Reqnroll.Generator.CodeDom;
using Reqnroll.Generator.UnitTestProvider;

namespace VariantsPlugin
{

    public class NUnitProviderExtended : NUnit3TestGeneratorProvider
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