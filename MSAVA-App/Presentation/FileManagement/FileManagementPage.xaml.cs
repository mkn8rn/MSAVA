using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System.Threading.Tasks;
using System.IO;
using MSAVA_App.Services.Pickers;
using System.ComponentModel;
using Microsoft.UI.Xaml.Media.Animation;

namespace MSAVA_App.Presentation.FileManagement;

public sealed partial class FileManagementPage : Page
{
    private bool _loadedOnce;
    private FileManagementModel? _vm;

    private Storyboard? _progressStoryboard;
    private const int InfoBarDurationMs = 10_000;

    public FileManagementPage()
    {
        this.InitializeComponent();
        this.Loaded += FileManagementPage_Loaded;
        this.Unloaded += FileManagementPage_Unloaded;
        this.DataContextChanged += FileManagementPage_DataContextChanged;
    }

    private static FileManagementModel? ResolveVm(object? dc)
        => dc as FileManagementModel
           ?? (dc?.GetType().GetProperty("Model")?.GetValue(dc) as FileManagementModel)
           ?? (dc?.GetType().GetProperty("ViewModel")?.GetValue(dc) as FileManagementModel);

    private async void FileManagementPage_Loaded(object sender, RoutedEventArgs e)
    {
        // If VM is already present by the time Loaded fires, trigger once.
        if (_loadedOnce) return;
        var vm = ResolveVm(DataContext);
        if (vm is not null)
        {
            AttachVm(vm);
            _loadedOnce = true;
            await vm.SaveAndRefreshAsync();
        }
    }

    private void FileManagementPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= VmOnPropertyChanged;
        }
        StopInfoBarProgress();
    }

    private async void FileManagementPage_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        // In some platforms DataContext is set after Loaded. Trigger once when VM arrives.
        if (_loadedOnce) return;
        var vm = ResolveVm(args.NewValue);
        if (vm is not null)
        {
            AttachVm(vm);
            _loadedOnce = true;
            await vm.SaveAndRefreshAsync();
        }
    }

    private void AttachVm(FileManagementModel vm)
    {
        if (_vm == vm) return;
        if (_vm is not null)
        {
            _vm.PropertyChanged -= VmOnPropertyChanged;
        }
        _vm = vm;
        _vm.PropertyChanged += VmOnPropertyChanged;
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileManagementModel.ShowUploadResult))
        {
            var vm = _vm;
            if (vm is null) return;
            if (vm.ShowUploadResult)
            {
                StartInfoBarProgress();
            }
            else
            {
                StopInfoBarProgress();
            }
        }
    }

    private void StartInfoBarProgress()
    {
        StopInfoBarProgress();

        // Reset bars to 0
        if (SuccessProgressBar is not null) SuccessProgressBar.Value = 0;
        if (ErrorProgressBar is not null) ErrorProgressBar.Value = 0;

        // Choose the visible bar
        var useSuccess = _vm?.UploadResultIsSuccess == true;
        var targetBar = useSuccess ? SuccessProgressBar : ErrorProgressBar;
        if (targetBar is null)
            return;

        var anim = new DoubleAnimation
        {
            From = 0,
            To = 100,
            Duration = new Duration(TimeSpan.FromMilliseconds(InfoBarDurationMs)),
            EnableDependentAnimation = true // ensure runs on all platforms
        };

        _progressStoryboard = new Storyboard();
        _progressStoryboard.Children.Add(anim);
        Storyboard.SetTarget(anim, targetBar);
        Storyboard.SetTargetProperty(anim, "Value");
        _progressStoryboard.Begin();
    }

    private void StopInfoBarProgress()
    {
        if (_progressStoryboard is not null)
        {
            try
            {
                _progressStoryboard.Stop();
            }
            catch { }
            _progressStoryboard = null;
        }

        // Snap to full on stop if still open
        if (SuccessInfoBar?.IsOpen == true)
        {
            SuccessProgressBar.Value = 100;
        }
        if (ErrorInfoBar?.IsOpen == true)
        {
            ErrorProgressBar.Value = 100;
        }
    }

    private void FilesNav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        // Prefer cached VM because DataContext can be different for NavigationView
        var vm = _vm ?? ResolveVm(DataContext) ?? ResolveVm(sender.DataContext);
        if (vm is null) return;

        var tag = (args.InvokedItemContainer as NavigationViewItem)?.Tag as string;
        switch (tag)
        {
            case "Exit":
                _ = vm.GoToMainAsync();
                break;
            case "YourFiles":
                vm.IsAddMode = false;
                break;
            case "AddFiles":
                _ = vm.StartAddAsync();
                break;
        }
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        var vm = _vm ?? ResolveVm(DataContext);
        if (vm is null) return;
        await vm.UploadAsync();
    }

    private void CopyResult_Click(object sender, RoutedEventArgs e)
    {
        var vm = _vm ?? ResolveVm(DataContext);
        if (vm is null) return;
        var text = vm.UploadResultCopyText ?? string.Empty;
        try
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        }
        catch
        {
            // ignore clipboard failures on some platforms
        }
    }

    private async void PickFile_Click(object sender, RoutedEventArgs e)
    {
        var vm = _vm ?? ResolveVm(DataContext);
        if (vm is null) return;

        var picked = await FilePicker.PickSingleAsync();
        if (picked is null) return;

        // If extension/name not set, default from picked file
        if (string.IsNullOrWhiteSpace(vm.NewFileName))
        {
            vm.NewFileName = picked.FileName;
        }
        if (string.IsNullOrWhiteSpace(vm.NewFileExtension))
        {
            vm.NewFileExtension = picked.Extension;
        }

        vm.NewFileStream = picked.Stream;
    }
}
