using System.Text.Json;
using Reqnroll.Configuration;
using Reqnroll.Generator.CodeDom;
using Reqnroll.Generator.Interfaces;
using Reqnroll.Generator.Plugins;
using Reqnroll.Generator.UnitTestConverter;
using Reqnroll.Generator.UnitTestProvider;
using Reqnroll.Infrastructure;
using Reqnroll.UnitTestProvider;

[assembly: GeneratorPlugin(typeof(VariantsPlugin.VariantsPlugin))]

namespace VariantsPlugin
{
    public class VariantsPlugin : IGeneratorPlugin
    {
        private string VariantKeyName = "variantkey";
        private string _variantKey;
        private string utp;

        public void Initialize(GeneratorPluginEvents pluginEvents, GeneratorPluginParameters pluginParameters,
            UnitTestProviderConfiguration unitTestProviderConfiguration)
        {
            // Hook into generator events
            utp = unitTestProviderConfiguration.UnitTestProvider;
            pluginEvents.CustomizeDependencies += OnCustomizeDependencies;
        }

        private void OnCustomizeDependencies(object sender, CustomizeDependenciesEventArgs e)
        {
            var objectContainer = e.ObjectContainer;
            var language = objectContainer.Resolve<ProjectSettings>().ProjectPlatformSettings.Language;
            var codeDomHelper = objectContainer.Resolve<CodeDomHelper>(language);
            var decoratorRegistry = objectContainer.Resolve<DecoratorRegistry>();
            var reqnrollConfiguration = objectContainer.Resolve<ReqnrollConfiguration>();
            using var doc = JsonDocument.Parse(reqnrollConfiguration.ConfigSourceText);
            var root = doc.RootElement;

            _variantKey = root.TryGetProperty(VariantKeyName, out var variantProp)
                ? variantProp.GetString()
                : "Operator";

            var isRetryActive = root.TryGetProperty("IsRetryActive", out var retryProp) && retryProp.GetBoolean();
            // Create custom unit test provider based on user defined config value
            if (string.IsNullOrEmpty(utp))
            {
                var c = objectContainer.Resolve<UnitTestProviderConfiguration>();
                utp = c.UnitTestProvider;

                if (string.IsNullOrEmpty(utp))
                    throw new Exception(
                        "Unit test provider not detected, please install as a nuget package described here: https://github.com/SpecFlowOSS/SpecFlow/wiki/SpecFlow-and-.NET-Core");
            }


            // If no provider exists, register your own
            var generatorProvider = GetGeneratorProviderFromConfig(codeDomHelper, utp);
            e.ObjectContainer.RegisterInstanceAs(generatorProvider);
            e.ObjectContainer.RegisterInstanceAs<IFeatureGeneratorProvider>(
                new FeatureGeneratorProviderExtended(new FeatureGeneratorExtended(generatorProvider, codeDomHelper,
                    reqnrollConfiguration, decoratorRegistry, _variantKey, isRetryActive)),
                "default"
            );
        }

        private IUnitTestGeneratorProvider GetGeneratorProviderFromConfig(CodeDomHelper codeDomHelper, string config) =>
            config switch
            {
                "nunit" => new NUnitProviderExtended(codeDomHelper, _variantKey),
                "xunit" => new XUnitProviderExtended(codeDomHelper, _variantKey),
                "mstest" => new MsTestProviderExtended(codeDomHelper, _variantKey),
                _ => new NUnitProviderExtended(codeDomHelper, _variantKey)
            };
    }
}