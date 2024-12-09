Feature: TestFeature
    @Operator:GR @Operator:DE
    Scenario: Bot Sweeper is triggered and action are applied to customers
        Given there is a candidate account for bot sweeper 
        When the bot sweeper is triggered through api
        Then action of bot sweeper are applied to the account