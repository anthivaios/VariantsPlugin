
# ReqNRoll Plugin: Tag Variant Extender

This plugin extends the functionality of ReqNRoll by enabling automatic generation of additional test cases based on tag variants, ensuring comprehensive test coverage.

---

## Features

| Feature                   | Description                                                                 |
|---------------------------|-----------------------------------------------------------------------------|
| **Automated Test Cases**  | Generates test cases for scenarios based on tag variants.                  |
| **Easy Integration**      | Works seamlessly with ReqNRoll workflows.                                  |
| **Customizable Tags**     | Allows you to define and configure your own tag variants.                  |
| **Improved Test Coverage**| Broadens coverage by handling all tag combinations.                        |

---
## Variants Plugin notes
One of the following unit test providers package should be installed:

- Reqnroll.NUnit
- Reqnroll.XUnit (Not tested thoroughly yet)

*Disclaimer:* Reqnroll.MSTest is not yet supported

---

## Installation

1. Add the plugin via NuGet:
   ```bash
   dotnet add package VariantsPlugin
   ```
2. Ensure your project references ReqNRoll and is set up correctly.

---

## Usage

### Step 1: Add Variant Key at reqnroll.json
```csharp
{
    "variantkey": "Browser"
}
```

### Step 2: Annotate Scenarios with Tags
Use tags in your scenarios:
```gherkin
@Browser:Chrome
@Browser:Firefox
Scenario: Access Dashboard
  Given the user is logged in
  When they navigate to the dashboard
  Then they should see their account summary
```

### Step 3: Access the variant
The variant key/value can then be accessed via the ScenarioContext static or injected class.
```ccharp
[Binding]
public sealed class Hooks
{
    private readonly ScenarioContext _scenarioContext;
    private IWebDriver _driver;

    public Hooks(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [BeforeScenario]
    public void BeforeScenario()
    {
        _scenarioContext.TryGetValue("Browser", out var browser);

        switch (browser)
        {
            case "Chrome":
                _driver = SetupChromeDriver();
                break;
            case "Firefox":
                _driver = SetupFirefoxDriver();
                break;
            default:
                _driver = SetupChromeDriver();
                break;
        }
        _scenarioContext.ScenarioContainer.RegisterInstanceAs(_driver);
    }
    ...
}
```

### Step 4: Run Your Tests
The plugin generates scenarios for all tag combinations.

---

## Example Output

For the following configuration in reqnroll.json:
```csharp
"variantkey": "Browser"
```
```csharp
@Browser:Chrome @Browser:Firefox @Browser:Safari
Scenario: This is a scenario
    Given this
    When that 
    Then this
```

A scenario tagged with `@Browser` will generate tests for:

| Browser   |
|-----------|
| Chrome    |
| Firefox   |
| Safari    |

In every Test generated, Scenario Context includes the specified Variant.
For Example in test "This_is_a_scenario_Chrome" ScenarioContext["Browser"] contains "Chrome". 
---

## Configuration Options

| Option                  | Description                                                                                                                       |
|-------------------------|-----------------------------------------------------------------------------------------------------------------------------------|
| **Variant Declaration** | Specify tags and their variants. Example: `"variantkey": "Browser"` "Browser" is now a variant                                    |
| **Tags with Variants**  | Add tags containing Variant key on Scenario or Feature level (can't be on both levels). Example: @Browser:Chrome @Browser:Firefox |

---

## Requirements

- **.NET Version**: .NET 7.0 or higher
- **Dependencies**: ReqNRoll installed and configured

---

## Contribution

We welcome contributions! If you have suggestions, bug reports, or feature requests:

1. Open an issue on the repository.
2. Submit a pull request with your changes.

---

## License

This plugin is licensed under the **MIT License**. See [LICENSE](LICENSE) for details.

---

## Contact

For support or inquiries, reach out to **AnThivaios** at **anthivaios@gmail.com**.

---
