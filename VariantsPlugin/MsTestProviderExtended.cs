using System.CodeDom;
using Reqnroll.Generator;
using Reqnroll.Generator.CodeDom;
using Reqnroll.Generator.UnitTestProvider;

namespace VariantsPlugin
{

    public class MsTestProviderExtended : MsTestV2GeneratorProvider
    {
        private readonly string _variantKey;
        public MsTestProviderExtended(CodeDomHelper codeDomHelper, string variantKey) : base(codeDomHelper)
        {
            _variantKey = variantKey;
        }

        public override void SetTestMethodCategories(TestClassGenerationContext generationContext, CodeMemberMethod testMethod, IEnumerable<string> scenarioCategories)
        {
            var variantValue = testMethod.Name.Split(new []{"__"}, StringSplitOptions.None).Last();
            var filteredCategories = scenarioCategories.Where(a => a.StartsWith(_variantKey) && a.EndsWith(variantValue));
            base.SetTestMethodCategories(generationContext, testMethod, scenarioCategories.Except(filteredCategories));
            
            var variant = scenarioCategories.FirstOrDefault(a => a.StartsWith(_variantKey) && a.EndsWith(variantValue));
            if (variant != null)
            {
                CodeDomHelper.AddAttribute(testMethod, "Microsoft.VisualStudio.TestTools.UnitTesting.TestPropertyAttribute", _variantKey, variantValue);
            }

            if (generationContext.CustomData.ContainsKey("featureCategories")
                && ((string[])generationContext.CustomData["featureCategories"]).Any(a => a.StartsWith(_variantKey)))
            {
                var dupeCounter = false;
                foreach (var item in testMethod.CustomAttributes.Cast<CodeAttributeDeclaration>().ToList())
                {
                    if (item.Name.Contains("TestCategory"))
                    {
                        var args = item.Arguments.Cast<CodeAttributeArgument>().Where(b => ((CodePrimitiveExpression)b.Value).Value.ToString().StartsWith(_variantKey) && !((CodePrimitiveExpression)b.Value).Value.ToString().EndsWith(variantValue));

                        if (args.Any())
                        {
                            testMethod.CustomAttributes.Remove(item);
                            return;
                        }
                        var args2 = item.Arguments.Cast<CodeAttributeArgument>().Where(b => ((CodePrimitiveExpression)b.Value).Value.ToString().StartsWith(variantValue) && ((CodePrimitiveExpression)b.Value).Value.ToString().EndsWith(variantValue));
                        if (args2.Any() && !dupeCounter)
                        {
                            testMethod.CustomAttributes.Remove(item);
                            dupeCounter = true;
                        }
                    }
                }
            }
        }

        public override void SetTestMethod(TestClassGenerationContext generationContext, CodeMemberMethod testMethod, string friendlyTestName)
        {
            var splitted = friendlyTestName.Split(new []{"__"}, StringSplitOptions.None);
            
            base.SetTestMethod(generationContext, testMethod, string.Join("", splitted));   
        }
    }
}