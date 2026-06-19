namespace MSAVA_App.UITests;

public static class Constants
{
    public const string WebAssemblyDefaultUri = "http://localhost:5000/";
    public const string iOSAppName = "MSAVA";
    public const string AndroidAppName = "MSAVA";
    public const string iOSDeviceNameOrId = "iPad Pro (12.9-inch) (3rd generation)";
    public const string FileManagementButtonAutomationId = "FileManagementButton";
    public const string FileManagementNavigationAutomationId = "FileManagementNavigation";

    public readonly static Platform CurrentPlatform = Platform.Browser;
    public readonly static Browser WebAssemblyBrowser = Browser.Chrome;
}
