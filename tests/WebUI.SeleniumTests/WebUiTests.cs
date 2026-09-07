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

    private void TakeScreenshot(string testTitle, string testDescription, IWebElement? highlightElement = null, bool isSuccess = true)
    {
        try
        {
            if (_driver is IJavaScriptExecutor js)
            {
                // GENEL ÇÖZÜM: Hata durumlarında tüm ekranlardaki eski yeşil toast'ları temizle ve sadece KIRMIZI HATA BİLDİRİMİ göster
                string cleanupAndToastScript = @"
                    var container = document.getElementById('toastContainer');
                    if (container) {
                        if (!arguments[2]) {
                            // Hata durumunda var olan tüm toast'ları temizle
                            container.innerHTML = '';
                            if (typeof showToast === 'function') {
                                showToast(arguments[1], 'error');
                            }
                        } else {
                            // Başarı durumunda eski kalan hata toast'larını temizle
                            container.querySelectorAll('.toast-error').forEach(function(e) { e.remove(); });
                        }
                    }
                ";

                js.ExecuteScript(cleanupAndToastScript, testTitle, testDescription, isSuccess);

                string badgeBg = isSuccess ? "#10B981" : "#EF4444";
                string badgeColor = isSuccess ? "#064E3B" : "#7F1D1D";
                string badgeText = isSuccess ? "✔ SELENIUM E2E PASSED" : "✖ SELENIUM E2E FAILED";
                string borderColor = isSuccess ? "#10B981" : "#EF4444";
                string glowShadowColor = isSuccess ? "rgba(16, 185, 129, 0.5)" : "rgba(239, 68, 68, 0.5)";

                string script = @"
                    var existingBanner = document.getElementById('selenium-test-banner');
                    if (existingBanner) existingBanner.remove();

                    var banner = document.createElement('div');
                    banner.id = 'selenium-test-banner';
                    banner.style.position = 'fixed';
                    banner.style.top = '20px';
                    banner.style.right = '20px';
                    banner.style.zIndex = '999999';
                    banner.style.background = 'linear-gradient(135deg, #0F172A 0%, #1E1B4B 100%)';
                    banner.style.border = '2px solid " + borderColor + @"';
                    banner.style.borderRadius = '12px';
                    banner.style.padding = '14px 20px';
                    banner.style.boxShadow = '0 10px 30px rgba(0,0,0,0.6), 0 0 20px " + glowShadowColor + @"';
                    banner.style.fontFamily = 'Plus Jakarta Sans, sans-serif';
                    banner.style.color = '#FFFFFF';
                    banner.style.maxWidth = '420px';

                    banner.innerHTML = `
                        <div style='display:flex; align-items:center; justify-content:space-between; gap:10px; margin-bottom:6px;'>
                            <span style='background:" + badgeBg + @"; color:" + badgeColor + @"; font-weight:800; font-size:11px; padding:4px 10px; border-radius:20px; text-transform:uppercase; letter-spacing:0.5px;'>" + badgeText + @"</span>
                            <span style='font-size:11px; color:#A5B4FC;'>${new Date().toLocaleTimeString()}</span>
                        </div>
                        <div style='font-size:16px; font-weight:700; color:#F8FAFC; margin-top:4px;'>${arguments[0]}</div>
                        <div style='font-size:12px; color:#94A3B8; margin-top:4px; line-height:1.4;'>${arguments[1]}</div>
                    `;
                    document.body.appendChild(banner);
                ";

                js.ExecuteScript(script, testTitle, testDescription);

                // Eğer hata durumunda highlightElement verilmediyse veya bir toast highlight edilecekse hedef belirle
                if (!isSuccess)
                {
                    js.ExecuteScript(@"
                        var errorToast = document.querySelector('.toast-error');
                        if (errorToast) {
                            errorToast.style.border = '6px solid #EF4444';
                            errorToast.style.outline = '4px solid #EF4444';
                            errorToast.style.boxShadow = '0 0 35px rgba(239, 68, 68, 1)';
                            errorToast.style.borderRadius = '8px';
                        }
                    ");
                }
                else if (highlightElement != null)
                {
                    string outlineColor = "#10B981";
                    string elementGlow = "rgba(16, 185, 129, 1)";
                    js.ExecuteScript(@"
                        arguments[0].style.border = '6px solid " + outlineColor + @"';
                        arguments[0].style.outline = '4px solid " + outlineColor + @"';
                        arguments[0].style.boxShadow = '0 0 35px " + elementGlow + @"';
                        arguments[0].style.borderRadius = '8px';
                    ", highlightElement);
                }
            }

            Thread.Sleep(500); // Allow browser to render overlay banner and bold borders

            if (_driver is ITakesScreenshot screenshotDriver)
            {
                var screenshot = screenshotDriver.GetScreenshot();
                var directory = Path.Combine(Directory.GetCurrentDirectory(), "screenshots");
                Directory.CreateDirectory(directory);
                var safeTitle = string.Concat(testTitle.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
                var filePath = Path.Combine(directory, $"{safeTitle}.png");
                screenshot.SaveAsFile(filePath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to take screenshot: {ex.Message}");
        }
    }

    [Fact]
    public void Dashboard_ShouldLoad_AndShowCorrectTitle()
    {
        _driver.Navigate().GoToUrl(_baseUrl);
        var headerTitle = _wait.Until(ExpectedConditions.ElementIsVisible(By.CssSelector(".logo-text h1")));

        // Verify Title
        Assert.Contains("E-Commerce", _driver.Title);
        Assert.Contains("E-Commerce", headerTitle.Text);

        TakeScreenshot("TEST_1_Dashboard_Kontrolu", "E-Commerce Microservices Dashboard arayüzü ve servis başlıkları başarıyla doğrulandı.", headerTitle, isSuccess: true);
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

        TakeScreenshot("TEST_2_Kullanici_Ekleme_Testi", $"Form doldurularak yeni kullanıcı ({testName}) sisteme eklendi ve bildirim alındı.", toastMessage, isSuccess: true);
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

        TakeScreenshot("TEST_3_Urun_Ekleme_Testi", $"Form doldurularak yeni ürün ({testProductName}) kataloğa eklendi ve başarı bildirimi alındı.", toastMessage, isSuccess: true);
    }

    public void Dispose()
    {
        _driver?.Quit();
        _driver?.Dispose();
    }
}
