using System.Text.RegularExpressions;
using Reqnroll.Parser;

namespace VariantsPlugin
{

    public class RetryHelper
    {
        public bool FeatureHasRetryTag { get; private set; }
        public int FeatureRetryCount { get; private set; }
        
        public List<string> GetRetryTag(ReqnrollFeature feature)
        {
            var tags = feature.Tags.Where(t => t.GetNameWithoutAt().Equals("retry", StringComparison.OrdinalIgnoreCase) 
                                               || Regex.IsMatch(t.GetNameWithoutAt(), @"^retry(?:\((\d+)\))?$", RegexOptions.IgnoreCase))
                .Select(t => t.GetNameWithoutAt()).ToList();
            FeatureHasRetryTag = tags.Count > 0;
            return tags;
        }
        public int GetRetriesNumber(List<string> listWithRetryTag)
        {
            FeatureHasRetryTag = true;
            var retryTag = listWithRetryTag.FirstOrDefault();
            if(retryTag.Equals("retry", StringComparison.OrdinalIgnoreCase))
                return 3;
            var match = Regex.Match(retryTag, @"\((\d+)\)");

            return int.Parse(match.Groups[1].Value);
        }

        public bool AnyScenarioHasRetryTag(ReqnrollFeature feature)
        {
            return feature.ScenarioDefinitions.Any(a => a.GetTags().Any(b => b.GetNameWithoutAt().Equals("retry", StringComparison.OrdinalIgnoreCase)
                                                                             || Regex.IsMatch(b.GetNameWithoutAt(), @"^retry(?:\((\d+)\))?$", RegexOptions.IgnoreCase)));
        }
        public void SetFeatureRetriesNumber(int number) => FeatureRetryCount = number;
    }
}