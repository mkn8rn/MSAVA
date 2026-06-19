namespace MSAVA_App.UITests;

public class Given_MainPage : TestBase
{
    [Test]
    public void When_FileManagementButtonIsTapped_NavigatesToFileManagement()
    {
        Query fileManagementButton = q => q.All().Marked(Constants.FileManagementButtonAutomationId);
        Query fileManagementNavigation = q => q.All().Marked(Constants.FileManagementNavigationAutomationId);

        App.WaitForElement(fileManagementButton);
        App.Tap(fileManagementButton);
        App.WaitForElement(fileManagementNavigation);

        TakeScreenshot("File management opened");
    }
}
