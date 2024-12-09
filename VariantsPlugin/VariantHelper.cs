using Gherkin.Ast;
using Reqnroll.Parser;

namespace VariantsPlugin
{

    public class VariantHelper
    {
        public string VariantKey { get; }
        public bool FeatureHasVariantTags { get; private set; }

        public VariantHelper(string variantKey)
        {
            VariantKey = variantKey;
        }

        public List<string> GetFeatureVariantTagValues(ReqnrollFeature feature)
        {
            var tags = FeatureTags(feature)?.Select(a => a.Name.Split(':')[1]).ToList();
            FeatureHasVariantTags = tags.Count > 0;
            return tags;
        }

        public List<string> GetScenarioVariantTagValues(StepsContainer scenario)
        {
            return scenario.GetTags()?.Where(a => a.Name.StartsWith($"@{VariantKey}"))?.Select(a => a.Name.Split(':')[1]).ToList();
        }

        public bool AnyScenarioHasVariantTag(ReqnrollFeature feature)
        {
            return feature.ScenarioDefinitions.Any(a => a.GetTags().Any(b => b.GetNameWithoutAt().StartsWith(VariantKey)));
        }

        public List<Tag> FeatureTags(ReqnrollFeature feature)
        {
            return feature.Tags?.Where(a => a.Name.StartsWith($"@{VariantKey}")).ToList();
        }
    }
}