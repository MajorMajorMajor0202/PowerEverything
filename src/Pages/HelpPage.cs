using EverythingExtension.Properties;

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

using System;

namespace EverythingExtension.Pages
{
    internal sealed partial class HelpPage : ContentPage
    {
        #region Fields

        private readonly MarkdownContent _mdContent;

        #endregion Fields

        #region Events

        internal event EventHandler? GoBackHomePage;

        #endregion Events

        #region Public Constructors

        public HelpPage()
        {
            Icon = IconHelpers.FromRelativePath("Assets\\Everything.png");
            Title = Resources.everything_help_title;
            Name = Resources.everything_text_preview;
            _mdContent = new MarkdownContent(Resources.everything_help_content + SyntaxReference);
            Commands = [
                    new CommandContextItem(Resources.everything_go_back,name:Resources.everything_go_back,action:()=>GoBackHomePage?.Invoke(this,EventArgs.Empty),result:CommandResult.GoBack()){ Icon = new IconInfo("\ue72b") }
                ];
        }

        #endregion Public Constructors

        #region Public Methods

        public override IContent[] GetContent() => [_mdContent];

        private const string SyntaxReference = """


        ## PowerEverything 查询语法速查

        | 用途 | 示例 |
        | --- | --- |
        | 同时匹配多个词 | `报告 预算` |
        | 匹配任意一个词 | `报告 \| 预算` |
        | 排除词 | `报告 !草稿` |
        | 完整短语 | `"annual report"` |
        | 只搜文件 / 文件夹 | `file: 报告` / `folder: 项目` |
        | 扩展名 | `ext:pdf;docx 报告` |
        | 限定目录（含子目录） | `"D:\My Projects\" 报告` |
        | 大小 | `size:>100mb` |
        | 最近修改 | `dm:last7days` |
        | 今天修改 | `dm:today` |
        | 正则（本插件） | `@^report.*\.pdf$` |

        开启「宏搜索」后，可以使用 `doc:`、`pic:`、`audio:`、`video:`、`zip:`、`code:`、`exe:`、`web:`、`font:`。
        例如 `code: QuickJump`。宏关闭时，请使用原生 `ext:` 语法。

        大小与修改时间显示在结果标签中；索引未提供的字段留空。

        `content:"关键词"` 是 Everything 的内容搜索语法。Everything 1.4 的内容通常未索引，
        会读取文件，建议同时限定目录及扩展名；Office/PDF 等格式还取决于系统 IFilter。

        [Everything 官方搜索语法](https://www.voidtools.com/support/everything/searching/)
        """;

        /*
        private static string GetMarkdownContent()
        {
            return @"
# CmdPal Everything 帮助文档
> CmdPal Everything 是一个用于 Windows 的命令面板扩展，它允许您通过命令面板快速访问 Everything 数据库中的文件和文件夹。

## Everything 功能（具体请参考[官网](https://www.voidtools.com/zh-cn/)文案）
- **快速访问**：通过命令面板，您可以快速找到并打开文件和文件夹，无需使用文件资源管理器。
- **搜索**：支持搜索文件和文件夹的名称、路径、扩展名等。
- **排序**：支持按名称、大小、修改日期等对搜索结果进行排序。
- **过滤**：支持过滤文件和文件夹的扩展名、类型等。
- **预览**：支持预览文件的内容，包括文本文件、图片、视频等。
- **运行**：支持以管理员身份运行文件。
- **复制路径**：支持复制文件和文件夹的路径。
- **更多功能**：支持更多高级功能，如创建快捷方式、重命名文件和文件夹等。

## CmdPal Everything 使用方法
1. 输入 `Everything`（或选择 `Everything 搜索`）以打开 Everything 命令面板。
2. 在命令面板中输入 `搜索关键字` 进行搜索。
3. 在搜索结果中，选择要打开的文件或文件夹，按 Enter 键进行打开。
4. 您还可以使用其他快捷键和功能(Ctrl+K)来操作搜索结果。
5. 搜索中可以使用内置 `宏` （设置中需要开启宏搜索）具有针对性的搜索，例如：`doc:、audio:、zip:、pic:、video:、web:、exe:`等。

## `宏` 搜索说明（支持的文件类型）
- `doc:`：搜索文档文件，如 .doc、.docx、.pdf 等。
  > c,chm,cpp,doc,dot,h,htm,html,mht,mhtml,nfo,pdf,pps,ppt,rtf,txt,vsd,wpd,wps,wri,xls,xml,txt,docx,htm,html,pdf,c,cpp,h,xls,odp,odt,ods,pptx,xlsx,csv,docx,ppsx,java,hpp,ini,dotx,xlsb

- `audio:`：搜索音频文件，如 .mp3、.wav、.flac 等。
  > aac,ac3,aif,aifc,aiff,au,cda,dts,fla,flac,gym,it,m1a,m2a,m4a,midi,mka,mod,mp2,mp3,mpa,ogg,ra,spc,rmi,snd,umx,vgm,vgz,voc,wav,wma,xm

- `zip:`：搜索压缩文件，如 .zip、.rar、.7z 等。
  > ace,arj,bz2,cab,gz,gzip,r00,r01,r02,r03,r04,r05,r06,r07,r08,r09,r10,r11,r12,r13,r14,r15,r16,r17,r18,r19,r20,r21,r22,r23,r24,r25,r26,r27,r28,r29,rar,tar,7z，zip

- `pic:`：搜索图片文件，如 .jpg、.png、.gif 等。
  > ani,bmp,gif,ico,jpe,jpeg,jpg,pcx,png,psd,tga,tif,tiff,wmf,wbmp,icl,jp2,mpng,raw,nef,wdp,hdp

- `video:`：搜索视频文件，如 .mp4、.avi、.mkv 等。
  > 3g2,3gp,3gp2,3gpp,amr,asf,avi,bik,d2v,dat,divx,drc,dsa,dsm,dss,dsv,flc,fli,flic,flv,ifo,ivf,m1v,m2v,m4b,m4p,m4v,mkv,mp2v,mp4,mpe,mpeg,mpg,mpv2,mov,ogm,pss,pva,qt,ram,ratdvd,rm,rmm,roq,rpm,smk,swf,tp,tpr,ts,vob,vp6,wm,wmp,wmv,rmvb

- `web:`：搜索网页文件，如 .html、.htm、.php 等。
  > html,htm,mht,php,css,sql,js

- `exe:`：搜索可执行文件，如 .exe、.msi、.bat 等。
  > exe,cmd,msi,msix

## 搜索参数
- 常规搜索
  > 输入关键词即可搜索

- 宏搜索
  > 宏标记:搜索关键词

  例如:

  ```
  pic:1212
  ```

- 自定义搜索结果数量（数量最大取值100，超过100会导致崩毁）
  > 用于自定义返回搜索结果；搜索规则：搜索关键词 count:数量；

  例如：
  ```
  hello count:10
  ```

- 开启正则搜索
   用 `@` 开头则启用正则搜索，需要注意的是，启用正则搜索时，无法启用`宏`搜索

## 其他

- 问题反馈联系方式
  - QQ：86477385

- 相关链接：
  - [Everything 官网](https://www.voidtools.com)
  - [PowerToys](https://github.com/microsoft/PowerToys)
  - [CmdPal](https://github.com/microsoft/PowerToys/tree/main/src/modules/cmdpal)
";
        }
        */

        #endregion Public Methods
    }
}
