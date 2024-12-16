Feature: TestFeature
    Scenario: Bot Sweeper is triggered and action are applied to customers
        Given there is a candidate account for bot sweeper 
        When the bot sweeper is triggered through api <Test2>
        Then action of bot sweeper are applied to the account <Test>
        Examples: 
        | Test | Test2 |
        | 1    | 3     |
        | 2    | 4     |