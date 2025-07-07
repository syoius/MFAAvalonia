<!-- markdownlint-disable MD033 MD041 -->
<p align="center">
  <img alt="LOGO" src="https://github.com/SweetSmellFox/MFAAvalonia/blob/master/MFAAvalonia/MFAAvalonia.ico" width="256" height="256" />
</p>

<div align="center">

# MFAAvalonia

<!-- prettier-ignore-start -->
<!-- markdownlint-disable-next-line MD036 -->
_✨ A universal GUI project for **[MAAFramework](https://github.com/MaaXYZ/MaaFramework)**  based
on **[Avalonia](https://github.com/AvaloniaUI/Avalonia)** ✨_
<!-- prettier-ignore-end -->

  <img alt="license" src="https://img.shields.io/github/license/SweetSmellFox/MFAAvalonia">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-≥%208-512BD4?logo=csharp">
  <img alt="platform" src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-blueviolet">
  <img alt="commit" src="https://img.shields.io/github/commit-activity/m/SweetSmellFox/MFAAvalonia">
  <img alt="stars" src="https://img.shields.io/github/stars/SweetSmellFox/MFAAvalonia?style=social">
  <a href="https://mirrorchyan.com/zh/projects?rid=MFAAvalonia&source=mfaagh-badge" target="_blank"><img alt="mirrorc" src="https://img.shields.io/badge/Mirror%20Chyan-%239af3f6?logo=countingworkspro&logoColor=4f46e5"></a>
</div>
<div align="center">

[English](./README_en.md) | [简体中文](./README.md)

</div>

## Preview

<p align="center">
  <img alt="preview" src="https://github.com/SweetSmellFox/MFAAvalonia/blob/master/MFAAvalonia/Img/preview.png" height="595" width="900" />
</p>

## Requirements

- .NET 8.0
- A resource project based on `MaaFramework`

## Documentation

### Usage

#### Automatic Installation

- Download workflows/install.yml from the project and modify the following:
  ```project name```, ```author name```, ```project title```, ```MAAxxx```
- Replace MAA project template .github/workflows/install.yml with the modified install.yml.
- Push the new version.

#### Manual Installation

- Download and extract the latest release.
- Copy all content from `maafw/assets/resource` to `MFAAvalonia/resource`.
- Copy the `maafw/assets/interface.json` file to the root directory of `MFAAvalonia/`.
- ***Modify*** the newly copied `interface.json` file.
- Below is an example:

 ```
{
  "resource": [
    {
      "name": "Official",
      "path": "{PROJECT_DIR}/resource/base"
    },
    {
      "name": "Bilibili",
      "path": [
        "{PROJECT_DIR}/resource/base",
        "{PROJECT_DIR}/resource/bilibili"
      ]
    }
  ],
  "task": [
    {
      "name": "Task",
      "entry": "Task"
    }
  ]
}
```

Modify it as follows:

```
{
  "name": "Project Name", // Default is null
  "version":  "Project Version", // Default is null
  "mirrorchyan_rid":  "Project ID (necessary fields downloaded from MirrorChyan)", // Default is null, for example, M9A
  "mirrorchyan_multiplatform": false, //Multi-platform flag, Default: false
  "url":  "Project URL (currently only supports Github)", // Default is null, for example, https://github.com/{GithubAccount}/{GithubRepo}
  "custom_title": "Custom Title", // Default is null, after using this field, the title bar will only show custom_title and version
  "resource": [
    {
      "name": "Official",
      "path": "{PROJECT_DIR}/resource/base"
    },
    {
      "name": "Bilibili",
      "path": [
        "{PROJECT_DIR}/resource/base",
        "{PROJECT_DIR}/resource/bilibili"
      ]
    }
  ],
  "task": [
    {
      "name": "Task",
      "entry": "Task Interface",
      "check": true,  // Default is false, whether the task is selected by default
      "doc": "Documentation",  // Default is null, displayed below the task setting options, supports rich text format (details below)
      "repeatable": true,  // Default is false, whether the task can be repeated
      "repeat_count": 1  // Default task repeat count, requires repeatable to be true
    }
  ]
}
```

Use controller[0] to control the default controller.

### `doc`String Formatting：

#### Use tags like`[color:red]`Text Content`[/color]` to define text styles.

#### Supported tags include:

- `[color:color_name]`: Color, such as`[color:red]`.

- ~~`[size:font_size]`: Font size, such as`[size:20]`.~~

- `[b]`: Bold.

- `[i]`: Italic.

- `[u]`: Underline.

- `[s]`：Strikethrough.

- `[align:left/center/right]`: Left-aligned, center-aligned, or right-aligned. Can only be applied to an entire line.

**Note: The above comments are for documentation purposes and are not recommended for actual usage.**

- Run the project

## Development Notes

- Some areas are not fully developed yet, and contributions are welcome.
- Placing `logo.ico` in the same directory as the exe file will replace the window icon.
- `MFAAvalonia` adds multi-language support for interfaces. After creating `zh-cn.json`,`zh-tw.json` and `en-us.json` in the same directory as `interface.json`, the names of docs and tasks and the names of options can be represented by keys. MFAAvalonia will automatically read the values corresponding to the keys in the files according to the language. If not, it defaults to the key.
- `MFAAvalonia` reads the `Announcement.md` file in the `resource` folder as the announcement, and automatically downloads a Changelog to serve as the announcement when updating resources.
- `MFAAvalonia` can be launched with a specific configuration file by using the startup parameter `-c config-name`, without requiring the `.json` suffix.

**Note: In MFA v1.1.6, the `focus` series fields were removed and replaced with `any focus`. The original fields are no longer available!**

- `focus` : *string* | *object*  
  Formats:
  ```
  "focus": {
    "start": "Task started",   // Note: *string* | *string[]*    
    "succeeded": "Task succeeded",   // Note: *string* | *string[]* 
    "failed": "Task failed",    // Note: *string* | *string[]* 
    "toast": "Toast notification"   // Note: *string* 
  }
  ```
  ```
   "focus": "Test"
  ```
  Equivalent to:
  ```
  "focus": {
    "start": "Test"
  }
    ```
Except for `toast`, all others support using tags like `[color:red]`text content`[/color]` to define text colors.

## License

**MFAAvalonia** is licensed under **[GPL-3.0 License](./LICENSE)**.

## Acknowledgements

### Open Source Projects

- **[SukiUI](https://github.com/kikipoulet/SukiUI)**\
  A Desktop UI Library for Avalonia.
- **[MaaFramework](https://github.com/MaaAssistantArknights/MaaFramework)**\
  Image recognition-based automation framework.
- **[Serilog](https://github.com/serilog/serilog)**\
  C# Logging Library
- **[Newtonsoft.Json](https://github.com/CommunityToolkit/dotnet)**\
  C# JSON Library
- **[MirrorChyan](https://github.com/MirrorChyan/docs)**\
  MirrorChyan Update Service
- **[AvaloniaExtensions.Axaml](https://github.com/dotnet9/AvaloniaExtensions)**\
  Syntax sugar for Avalonia UI development
- **[CalcBindingAva](https://github.com/netwww1/CalcBindingAva)**\
  CalcBinding is an advanced Binding markup extension that allows you to write calculated binding expressions in xaml,
  without custom converter

### Contributors

Thanks to all contributors who helped build **MFAAvalonia** .

<a href="https://github.com/SweetSmellFox/MFAAvalonia/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=SweetSmellFox/MFAAvalonia&max=1000" alt="Contributors to MFAAvalonia"/>
</a>