using System.CodeDom;
using Gherkin.Ast;
using Reqnroll.Generator;
using Reqnroll.Parser;
using Reqnroll.Tracing;

namespace VariantsPlugin
{

    public static class ReqnrollFeatureExtensions
    {
        public static bool HasFeatureBackground(this ReqnrollFeature feature)
        {
            return feature.Background != null;
        }

        public static bool CanUseFirstColumnAsName(this IEnumerable<TableRow> tableBody)
        {
            bool func(TableRow r) => !r.Cells.Any();
            if (tableBody.Any(func))
                return false;
            return tableBody.Select(r => r.Cells.First().Value.ToIdentifier()).Distinct().Count() == tableBody.Count();
        }

        public static ParameterSubstitution CreateParamToIdentifierMapping(this ScenarioOutline scenarioOutline)
        {
            var parameterSubstitution = new ParameterSubstitution();

            foreach (var cell in scenarioOutline.Examples.First().TableHeader.Cells)
                parameterSubstitution.Add(cell.Value, cell.Value.ToIdentifierCamelCase());

            return parameterSubstitution;
        }

        public static void ValidateExampleSetConsistency(this ScenarioOutline scenarioOutline)
        {
            if (scenarioOutline.Examples.Count() <= 1)
                return;
            var firstExamplesHeader = scenarioOutline.Examples.First().TableHeader.Cells.Select(c => c.Value).ToArray();
            var source = scenarioOutline.Examples.Skip(1);
            IEnumerable<string> func(Examples examples) => examples.TableHeader.Cells.Select(c => c.Value);
            if (source.Select(func).Any(paramNames => !paramNames.SequenceEqual(firstExamplesHeader)))
                throw new TestGeneratorException("The example sets must provide the same parameters.");
        }

        public static IEnumerable<string> GetTagsExcept(this IEnumerable<Tag> tags, string tag)
        {
            return tags.Where(t => !t.Name.Equals(tag, StringComparison.InvariantCultureIgnoreCase)).Select(t => t.GetNameWithoutAt());
        }

        public static bool HasTag(this IEnumerable<Tag> tags, string tag)
        {
            return tags.Any(t => t.Name.Equals(tag, StringComparison.InvariantCultureIgnoreCase));
        }

        public static CodeExpression GetStringArrayExpression(this IEnumerable<Tag> tags)
        {
            if (!tags.Any())
                return new CodeCastExpression(typeof(string[]), new CodePrimitiveExpression(null));
            return new CodeArrayCreateExpression(typeof(string[]), tags.Select(tag => new CodePrimitiveExpression(tag.GetNameWithoutAt())).Cast<CodeExpression>().ToArray());
        }

        public static IEnumerable<Tag> ConcatTags(this IEnumerable<Tag> tags, params IEnumerable<Tag>[] tagLists)
        {
            return tagLists.Where(tagList => tagList != null).SelectMany(tagList => tagList).Concat(tags);
        }

        public static ReqnrollStep AsRecnrollStep(this Step step)
        {
            if (step is ReqnrollStep recnrollStep) return recnrollStep;
            throw new TestGeneratorException("The step must be a RecnrollStep.");
        }
    }
}