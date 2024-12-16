using Reqnroll;
using Reqnroll.BoDi;

namespace TestProject
{
    [Binding]
    public class TestSteps
    {
        private readonly ScenarioContext _scenarioContext;
        private readonly IObjectContainer _objectContainer;
        public TestSteps(ScenarioContext scenarioContext, IObjectContainer objectContainer)
        {
            _objectContainer = objectContainer;
            _scenarioContext = scenarioContext;
        }
        [Given(@"there is a candidate account for bot sweeper")]
        public Task GivenAntifraudRecordsEndpointIsCalledForCustomer()
        {
            Console.WriteLine(_scenarioContext.ContainsValue("operator"));
            return Task.CompletedTask;
        }
        [When(@"the bot sweeper is triggered through api (.*)")]
        public Task GivenAntifraudRecordsEndpointIsCalledForCustomer2(string ruleId)
        {
            Console.WriteLine("When Step");
            return Task.CompletedTask;
        }
        [Then(@"action of bot sweeper are applied to the account (.*)")]
        public Task GivenAntifraudRecordsEndpointIsCalledForCustomer3(string ruleId)
        {
            Console.WriteLine(ruleId);
            return Task.CompletedTask;
        }
    }
}