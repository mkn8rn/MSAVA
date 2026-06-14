namespace MSAVA_App.Presentation.FileManagement;

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MSAVA_App.Services.Files;
using MSAVA_App.Services.Navigation;
using MSAVA_App.Presentation.Welcome;
using MSAVA_Shared.Models;
using Uno.Extensions;
using Uno.Extensions.Navigation;

public partial record FileManagementModel : INotifyPropertyChanged
{
    private static readonly TimeSpan UploadResultAutoDismissDelay = TimeSpan.FromSeconds(10);

    private readonly IDispatcher _dispatcher;
    private readonly FileRetrievalService _filesService;
    private readonly NavigationService _navigation;
    private readonly FileUploadClientService _uploadService;
    private CancellationTokenSource? _uploadResultDismissal;

    public FileManagementModel(IDispatcher dispatcher, FileRetrievalService filesService, NavigationService navigation, FileUploadClientService uploadService)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _filesService = filesService ?? throw new ArgumentNullException(nameof(filesService));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _uploadService = uploadService ?? throw new ArgumentNullException(nameof(uploadService));
        Files = new ObservableCollection<SearchFileDataDTO>();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoading)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoaded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanUpload)));
        }
    }

    public bool IsLoaded => !IsLoading;

    public bool CanUpload => IsLoaded && !IsUploading;

    public ObservableCollection<SearchFileDataDTO> Files { get; }

    // UI state: add new file form visibility
    private bool _isAddMode;
    public bool IsAddMode
    {
        get => _isAddMode;
        set
        {
            if (_isAddMode == value) return;
            _isAddMode = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAddMode)));
        }
    }

    // Upload in-progress state
    private bool _isUploading;
    public bool IsUploading
    {
        get => _isUploading;
        private set
        {
            if (_isUploading == value) return;
            _isUploading = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUploading)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanUpload)));
        }
    }

    // Result/status info bar state
    private bool _showUploadResult;
    public bool ShowUploadResult
    {
        get => _showUploadResult;
        set { if (_showUploadResult == value) return; _showUploadResult = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowUploadResult))); }
    }

    private bool _uploadResultIsSuccess;
    public bool UploadResultIsSuccess
    {
        get => _uploadResultIsSuccess;
        set { if (_uploadResultIsSuccess == value) return; _uploadResultIsSuccess = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UploadResultIsSuccess))); }
    }

    private string _uploadResultTitle = string.Empty;
    public string UploadResultTitle
    {
        get => _uploadResultTitle; set { if (_uploadResultTitle == value) return; _uploadResultTitle = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UploadResultTitle))); }
    }

    private string _uploadResultBody = string.Empty;
    public string UploadResultBody
    {
        get => _uploadResultBody; set { if (_uploadResultBody == value) return; _uploadResultBody = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UploadResultBody))); }
    }

    private string _uploadResultCopyText = string.Empty;
    public string UploadResultCopyText
    {
        get => _uploadResultCopyText; set { if (_uploadResultCopyText == value) return; _uploadResultCopyText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UploadResultCopyText))); }
    }

    // Form fields for SaveFileFromFormFileDTO
    private string _newFileName = string.Empty;
    public string NewFileName
    {
        get => _newFileName; set { if (_newFileName == value) return; _newFileName = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewFileName))); }
    }

    private string _newFileExtension = string.Empty;
    public string NewFileExtension
    {
        get => _newFileExtension; set { if (_newFileExtension == value) return; _newFileExtension = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewFileExtension))); }
    }

    private string _newDescription = string.Empty;
    public string NewDescription
    {
        get => _newDescription; set { if (_newDescription == value) return; _newDescription = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewDescription))); }
    }

    private string _newTagsCsv = string.Empty;
    public string NewTagsCsv
    {
        get => _newTagsCsv; set { if (_newTagsCsv == value) return; _newTagsCsv = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewTagsCsv))); }
    }

    private string _newCategoriesCsv = string.Empty;
    public string NewCategoriesCsv
    {
        get => _newCategoriesCsv; set { if (_newCategoriesCsv == value) return; _newCategoriesCsv = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewCategoriesCsv))); }
    }

    private Guid _newAccessGroupId = Guid.Empty;
    public Guid NewAccessGroupId
    {
        get => _newAccessGroupId; set { if (_newAccessGroupId == value) return; _newAccessGroupId = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewAccessGroupId))); }
    }

    private bool _newPublicViewing;
    public bool NewPublicViewing
    {
        get => _newPublicViewing; set { if (_newPublicViewing == value) return; _newPublicViewing = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewPublicViewing))); }
    }

    private bool _newPublicDownload;
    public bool NewPublicDownload
    {
        get => _newPublicDownload; set { if (_newPublicDownload == value) return; _newPublicDownload = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewPublicDownload))); }
    }

    private Stream? _newFileStream;
    public Stream? NewFileStream
    {
        get => _newFileStream; set { if (_newFileStream == value) return; _newFileStream = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewFileStream))); }
    }

    public async Task SaveAndRefreshAsync(CancellationToken ct = default)
    {
        if (IsLoading) return;

        await _dispatcher.ExecuteAsync(() => IsLoading = true);

        try
        {
            var data = await _filesService.GetAllFileMetadataAsync(ct);

            await _dispatcher.ExecuteAsync(() =>
            {
                Files.Clear();
                foreach (var item in data)
                {
                    Files.Add(item);
                }
            });
        }
        finally
        {
            await _dispatcher.ExecuteAsync(() => IsLoading = false);
        }
    }

    public Task Save() => SaveAndRefreshAsync();

    public async Task StartAddAsync()
    {
        await _dispatcher.ExecuteAsync(() => IsAddMode = true);
    }

    private async Task ResetAddFormAsync()
    {
        await _dispatcher.ExecuteAsync(() =>
        {
            // Keep user on the add form; just clear fields
            NewFileName = string.Empty;
            NewFileExtension = string.Empty;
            NewDescription = string.Empty;
            NewTagsCsv = string.Empty;
            NewCategoriesCsv = string.Empty;
            NewAccessGroupId = Guid.Empty;
            NewPublicViewing = false;
            NewPublicDownload = false;
            NewFileStream = null;
        });
    }

    public async Task UploadAsync(CancellationToken ct = default)
    {
        if (NewFileStream is null) return;
        var tags = (NewTagsCsv ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var categories = (NewCategoriesCsv ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        CancelPendingUploadResultDismissal();

        await _dispatcher.ExecuteAsync(() =>
        {
            ShowUploadResult = false;
            IsUploading = true;
        });

        try
        {
            var outcome = await _uploadService.CreateFileFromFormFileAsync(
                fileName: NewFileName,
                fileExtension: NewFileExtension,
                fileStream: NewFileStream,
                accessGroupId: NewAccessGroupId,
                tags: tags,
                categories: categories,
                description: NewDescription,
                publicViewing: NewPublicViewing,
                publicDownload: NewPublicDownload,
                ct: ct);

            await _dispatcher.ExecuteAsync(() =>
            {
                UploadResultIsSuccess = outcome.Success;
                UploadResultTitle = outcome.Success ? "Upload complete" : "Upload failed";
                UploadResultBody = $"Status: {outcome.StatusCode}\n" + (outcome.Success ? $"Id: {outcome.Id}" : $"Error: {outcome.Error}");
                UploadResultCopyText = outcome.Success ? outcome.Id ?? string.Empty : outcome.Error ?? string.Empty;
                ShowUploadResult = true;
            });

            if (outcome.Success)
            {
                await ResetAddFormAsync();
                await SaveAndRefreshAsync(ct);
            }
        }
        finally
        {
            await _dispatcher.ExecuteAsync(() => IsUploading = false);
            ScheduleUploadResultAutoDismiss();
        }
    }

    private void ScheduleUploadResultAutoDismiss()
    {
        if (!ShowUploadResult) return;

        CancelPendingUploadResultDismissal();

        var dismissal = new CancellationTokenSource();
        _uploadResultDismissal = dismissal;
        _ = DismissUploadResultAfterDelayAsync(dismissal);
    }

    private async Task DismissUploadResultAfterDelayAsync(CancellationTokenSource dismissal)
    {
        try
        {
            await Task.Delay(UploadResultAutoDismissDelay, dismissal.Token);
            await _dispatcher.ExecuteAsync(() =>
            {
                if (ReferenceEquals(_uploadResultDismissal, dismissal))
                {
                    ShowUploadResult = false;
                }
            });
        }
        catch (OperationCanceledException)
        {
            // A newer upload result owns the current InfoBar lifetime.
        }
        finally
        {
            if (ReferenceEquals(_uploadResultDismissal, dismissal))
            {
                _uploadResultDismissal = null;
            }

            dismissal.Dispose();
        }
    }

    private void CancelPendingUploadResultDismissal()
    {
        var pendingDismissal = _uploadResultDismissal;
        if (pendingDismissal is null) return;

        _uploadResultDismissal = null;
        pendingDismissal.Cancel();
    }
    
    // Navigate back to main page
    public async Task GoToMainAsync(CancellationToken ct = default)
    {
        await _navigation.NavigateTo<MainModel>(this, ct: ct);
    }
}
