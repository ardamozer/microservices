using System;
using System.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;
using Xunit;

namespace WebUI.SeleniumTests;

public class WebUiTests : IDisposable
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly string _baseUrl;

    public WebUiTests()
    {
        _baseUrl = Environment.GetEnvironmentVariable("TARGET_URL") ?? "http://localhost:8080";

        var options = new ChromeOptions();
        options.AddArgument("--headless=new");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--disable-gpu");
        options.AddArgument("--window-size=1920,1080");

        // Allow custom Chrome binary path if specified in CI/CD environment
        var chromeBin = Environment.GetEnvironmentVariable("CHROME_BIN");
        if (!string.IsNullOrEmpty(chromeBin))
        {
            options.BinaryLocation = chromeBin;
        }

        _driver = new ChromeDriver(options);
        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Dashboard_ShouldLoad_AndShowCorrectTitle()
    {
        _driver.Navigate().GoToUrl(_baseUrl);

        // Verify Title
        Assert.Contains("E-Commerce", _driver.Title);

        // Verify main header exists
        var headerTitle = _wait.Until(ExpectedConditions.ElementIsVisible(By.CssSelector(".logo-text h1")));
        Assert.NotNull(headerTitle);
        Assert.Contains("E-Commerce", headerTitle.Text);
    }

    [Fact]
    public void AddUserForm_ShouldCreateUserSuccessfully()
    {
        _driver.Navigate().GoToUrl(_baseUrl);

        var nameInput = _wait.Until(ExpectedConditions.ElementIsVisible(By.Id("userName")));
        var emailInput = _driver.FindElement(By.Id("userEmail"));
        var submitButton = _driver.FindElement(By.CssSelector("#addUserForm button[type='submit']"));

        string testName = "Selenium User " + Guid.NewGuid().ToString("N").Substring(0, 6);
        string testEmail = $"selenium_{Guid.NewGuid().ToString("N").Substring(0, 6)}@example.com";

        nameInput.Clear();
        nameInput.SendKeys(testName);

        emailInput.Clear();
        emailInput.SendKeys(testEmail);

        submitButton.Click();

        // Verify toast notification or presence in user list
        var toastMessage = _wait.Until(ExpectedConditions.ElementIsVisible(By.ClassName("toast")));
        Assert.NotNull(toastMessage);
    }

    [Fact]
    public void AddProductForm_ShouldCreateProductSuccessfully()
    {
        _driver.Navigate().GoToUrl(_baseUrl);

        var productNameInput = _wait.Until(ExpectedConditions.ElementIsVisible(By.Id("productName")));
        var productPriceInput = _driver.FindElement(By.Id("productPrice"));
        var productStockInput = _driver.FindElement(By.Id("productStock"));
        var submitButton = _driver.FindElement(By.CssSelector("#addProductForm button[type='submit']"));

        string testProductName = "Selenium Product " + Guid.NewGuid().ToString("N").Substring(0, 6);

        productNameInput.Clear();
        productNameInput.SendKeys(testProductName);

        productPriceInput.Clear();
        productPriceInput.SendKeys("299.99");

        productStockInput.Clear();
        productStockInput.SendKeys("50");

        submitButton.Click();

        // Verify toast message
        var toastMessage = _wait.Until(ExpectedConditions.ElementIsVisible(By.ClassName("toast")));
        Assert.NotNull(toastMessage);
    }

    public void Dispose()
    {
        _driver?.Quit();
        _driver?.Dispose();
    }
}
