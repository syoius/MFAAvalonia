using CommunityToolkit.Mvvm.Input;
using Org.BouncyCastle.Ocsp;
using System;
using System.Diagnostics;
using System.Windows.Input;

namespace MFAAvalonia.Helper;

public class MFAUrls
{
    public const string GitHub = "https://github.com/syoius/MaaYuan";

    public const string GitHubIssues = $"{GitHub}/issues";

    public const string GitHubToken = $"https://github.com/settings/tokens";

    public const string NewIssueUri = $"{GitHubIssues}/new?assignees=&labels=bug&template=cn-bug-report.yaml";

    public const string PurchaseLink = "https://mirrorchyan.com?rid=MaaYuan&source=maayuan-gui";

    public const string MaayuanDoc = "https://docs.qq.com/aio/DS1BMQmpiQkdOb1RT";
}
