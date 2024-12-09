using Newtonsoft.Json.Linq;
using Reqnroll.Configuration;
using Reqnroll.Generator.CodeDom;
using Reqnroll.Generator.Interfaces;
using Reqnroll.Generator.Plugins;
using Reqnroll.Generator.UnitTestConverter;
using Reqnroll.Infrastructure;
using Reqnroll.UnitTestProvider;

[assembly: GeneratorPlugin(typeof(VariantsPlugin.VariantsPlugin))]

namespace VariantsPlugin
{

    public class VariantsPlugin : IGeneratorPlugin
    {
        private string VariantKeyName = "variantkey";
        private string _variantKey;
        public void Initialize(GeneratorPluginEvents pluginEvents, GeneratorPluginParameters pluginParameters,
            UnitTestProviderConfiguration unitTestProviderConfiguration)
        {
            // Hook into generator events
            pluginEvents.CustomizeDependencies += OnCustomizeDependencies;
        }

        private void OnCustomizeDependencies(object sender, CustomizeDependenciesEventArgs e)
        {
            
            var objectContainer = e.ObjectContainer;
            var language = objectContainer.Resolve<ProjectSettings>().ProjectPlatformSettings.Language;
            var codeDomHelper = objectContainer.Resolve<CodeDomHelper>(language);
            var decoratorRegistry = objectContainer.Resolve<DecoratorRegistry>();
            var reqnrollConfiguration = objectContainer.Resolve<ReqnrollConfiguration>();
            var configAsJObject = JObject.Parse(reqnrollConfiguration.ConfigSourceText);
            _variantKey = configAsJObject.ContainsKey(VariantKeyName)? configAsJObject.GetValue(VariantKeyName).ToString() : "Operator";
            var generatorProvider = new NUnitProviderExtended(codeDomHelper, _variantKey);
            var customFeatureGenerator = new FeatureGeneratorExtended(generatorProvider, codeDomHelper,
                reqnrollConfiguration, decoratorRegistry, _variantKey);
            
            var customFeatureGeneratorProvider = new FeatureGeneratorProviderExtended(customFeatureGenerator);
            e.ObjectContainer.RegisterInstanceAs(generatorProvider);
            e.ObjectContainer.RegisterInstanceAs(customFeatureGenerator);
            e.ObjectContainer.RegisterInstanceAs<IFeatureGeneratorProvider>(customFeatureGeneratorProvider, "default");
        }
    }
}