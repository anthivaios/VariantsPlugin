using Reqnroll;

namespace TestProject
{
    [Binding]
    public class TestSteps
    {
        [Given(@"there is a candidate account for bot sweeper")]
        public Task GivenAntifraudRecordsEndpointIsCalledForCustomer()
        {
            Console.WriteLine("Given Step");
            return Task.CompletedTask;
        }
        [When(@"the bot sweeper is triggered through api")]
        public Task GivenAntifraudRecordsEndpointIsCalledForCustomer2()
        {
            Console.WriteLine("When Step");
            return Task.CompletedTask;
        }
        [Then(@"action of bot sweeper are applied to the account")]
        public Task GivenAntifraudRecordsEndpointIsCalledForCustomer3()
        {
            Console.WriteLine("Then Step");
            return Task.CompletedTask;
        }
    }
}