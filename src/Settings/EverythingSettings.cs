// Copyright (c) Microsoft Corporation The Microsoft Corporation licenses this file to you under the
// MIT license. See the LICENSE file in the project root for more information.

using EverythingExtension.Internal;
using EverythingExtension.Properties;

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.VisualBasic;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace EverythingExtension.Settings
{
    internal sealed class EverythingSettings : JsonSettingsManager
    {
        #region Fields

        public static readonly int DefaultMaxSearchCount = 200;

        #endregion Fields



        #region Fields

        private const string Namespace = "everything";

        private static readonly List<ChoiceSetSetting.Choice> _searchFoldersFirstChoices = new List<ChoiceSetSetting.Choice>
        {
            new ChoiceSetSetting.Choice(Resources.everything_searchFoldersFirst_ascending,"0"),
            new ChoiceSetSetting.Choice(Resources.everything_searchFoldersFirst_always,"1"),
            new ChoiceSetSetting.Choice(Resources.everything_searchFoldersFirst_never,"2"),
            new ChoiceSetSetting.Choice(Resources.everything_searchFoldersFirst_descending,"3"),
        };

        private readonly ToggleSetting _macroEnabled = new(
                    Namespaced(nameof(MacroEnabled)),
            Resources.everything_macro_enabled,
            Resources.everything_macro_enabled_description,
            true);

        private readonly TextSetting _maxSearchCount = new(
            Namespaced(nameof(MaxSearchCount)),
            Resources.everything_max_search_result_limit,
            Resources.everything_max_search_result_limit_description,
            DefaultMaxSearchCount.ToString(CultureInfo.InvariantCulture));

        private readonly ToggleSetting _matchCase = new(
           Namespaced(nameof(MatchCase)),
           Resources.everything_matchCase_enabled,
           Resources.everything_matchCase_enabled_description,
           false);

        private readonly ToggleSetting _matchDiacritics = new(
          Namespaced(nameof(MatchDiacritics)),
          Resources.everything_matchDiacritics_enabled,
          Resources.everything_matchDiacritics_enabled_description,
          false);

        private readonly ToggleSetting _matchWholeWords = new(
          Namespaced(nameof(MatchWholeWords)),
          Resources.everything_matchWholeWords_enabled,
          Resources.everything_matchWholeWords_enabled_description,
          false);

        private readonly ToggleSetting _matchPath = new(
        Namespaced(nameof(MatchPath)),
        Resources.everything_matchPath_enabled,
        Resources.everything_matchPath_enabled_description,
        false);

        private readonly ToggleSetting _matchPrefix = new(
      Namespaced(nameof(MatchPrefix)),
      Resources.everything_matchPrefix_enabled,
      Resources.everything_matchPrefix_enabled_description,
      false);

        private readonly ToggleSetting _matchSuffix = new(
      Namespaced(nameof(MatchSuffix)),
      Resources.everything_matchSuffix_enabled,
      Resources.everything_matchSuffix_enabled_description,
      false);

        private readonly ToggleSetting _ignorePunctuation = new(
      Namespaced(nameof(IgnorePunctuation)),
      Resources.everything_ignorePunctuation_enabled,
      Resources.everything_ignorePunctuation_enabled_description,
      false);

        private readonly ToggleSetting _ignoreWhitespace = new(
      Namespaced(nameof(IgnoreWhitespace)),
      Resources.everything_ignoreWhitespace_enabled,
      Resources.everything_ignoreWhitespace_enabled_description,
      false);

        private readonly ChoiceSetSetting _searchFoldersFirst = new(
     Namespaced(nameof(SearchFoldersFirst)),
     Resources.everything_searchFoldersFirst_enabled,
     Resources.everything_searchFoldersFirst_enabled_description,
     _searchFoldersFirstChoices);

        // —— 以下为 fork 新增的设置（功能对标 ECP，均为原生实现）——

        /// <summary>搜索前缀：每次查询自动加到最前面的 Everything 语法。</summary>
        private readonly TextSetting _queryPrefix = new(
            Namespaced(nameof(QueryPrefix)),
            "搜索前缀",
            "每次搜索自动附加的 Everything 语法，如 ext:pdf",
            string.Empty);

        /// <summary>正则搜索总开关。</summary>
        private readonly ToggleSetting _useRegex = new(
            Namespaced(nameof(UseRegex)),
            "正则搜索",
            "把查询按正则表达式匹配",
            false);

        private static readonly List<ChoiceSetSetting.Choice> _sortByChoices = new List<ChoiceSetSetting.Choice>
        {
            new ChoiceSetSetting.Choice("默认（Everything 自身顺序）", "default"),
            new ChoiceSetSetting.Choice("名称", "name"),
            new ChoiceSetSetting.Choice("修改日期", "date-modified"),
            new ChoiceSetSetting.Choice("大小", "size"),
            new ChoiceSetSetting.Choice("运行次数", "run-count"),
        };

        /// <summary>结果排序。</summary>
        private readonly ChoiceSetSetting _sortBy = new(
            Namespaced("SortBy"),
            "结果排序",
            "查询里写 sort: 时以查询为准",
            _sortByChoices);

        /// <summary>「发送到指定程序」的程序路径。</summary>
        private readonly TextSetting _sendToProgram = new(
            Namespaced(nameof(SendToProgram)),
            "发送到指定程序",
            "右键「发送到指定程序」使用的程序路径，留空不显示",
            string.Empty);

        /// <summary>QuickLook 预览的快捷键。</summary>
        private readonly TextSetting _previewHotkey = new(
            Namespaced("PreviewHotkey"),
            "预览快捷键",
            "选中文件后按该组合键预览，如 F1、Ctrl+Shift+P",
            "F1");

        //private readonly  _macroDisabled = new(

        #endregion Fields

        #region Private Methods

        private static string Namespaced(string propertyName) => $"{Namespace}.{propertyName}";

        private static string SettingsJsonPath()
        {
            var directory = Utilities.BaseSettingsPath("Microsoft.CmdPal");
            Directory.CreateDirectory(directory);

            // now, the state is just next to the exe
            return Path.Combine(directory, "settings.json");
        }

        #endregion Private Methods

        #region Public Constructors

        public EverythingSettings() : base()
        {
            FilePath = SettingsJsonPath();

            // 排列顺序 = 设置表单里的显示顺序：先搜索行为，再右键菜单相关，最后匹配选项。
            Settings.Add(_maxSearchCount);
            Settings.Add(_sortBy);
            Settings.Add(_searchFoldersFirst);
            Settings.Add(_queryPrefix);
            Settings.Add(_useRegex);
            Settings.Add(_macroEnabled);
            Settings.Add(_previewHotkey);
            Settings.Add(_sendToProgram);
            Settings.Add(_matchCase);
            Settings.Add(_matchWholeWords);
            Settings.Add(_matchPath);
            Settings.Add(_matchPrefix);
            Settings.Add(_matchSuffix);
            Settings.Add(_matchDiacritics);
            Settings.Add(_ignorePunctuation);
            Settings.Add(_ignoreWhitespace);

            // 必须先 Add 再 Load：JsonSettingsManager.LoadSettings() 内部走 Settings.Update()，
            // 它只对【已经 Add 进去】的设置项生效；顺序反了的话上次保存的值一个都读不回来
            // （上游原来的写法就是反的，导致匹配选项等设置在重启后恢复默认值）。
            LoadSettings();

            Settings.SettingsChanged += (_, _) => SaveSettings();
        }

        #endregion Public Constructors

        internal static readonly EverythingSettings Instance = new();

        // 3g2,3gp,3gp2,3gpp,amr, asf,avi, bik,d2v,divx,drc,dsa,dsm,dss,dsv, flc,fli,flic,flv,
        // ifo,ivf,m1v,m2v, m4v,mkv,mp2v,mp4, mpe,mpeg,mpg, mpv2, mov, ogm,
        // pss,pva,qt,ram,ratdvd,rm,rmm,rmvb,roq,rpm, smk,swf,tp,tpr,ts,vob,vp6, wm,wmp,wmv dat,m4b,m4p,
        internal List<MacroSettings> Macros { get; } =
                [
            new("audio", "aac,ac3,adt,adts,aif,aifc,aiff,amr,ape,au,cda,dts,ec3,fla,flac,it,lpcm,m1a,m2a,m3u,m3u8,m4a,m4b,m4p,mid,midi,mka,mod,mp2,mp3,mpa,mpc,oga,ogg,opus,ra,rmi,snd,spc,umx,voc,wav,wax,weba,wma,xm") ,
            new("zip", "7z,ace,arj,bz2,cab,gz,gzip,jar,r00,r01,r02,r03,r04,r05,r06,r07,r08,r09,r10,r11,r12,r13,r14,r15,r16,r17,r18,r19,r20,r21,r22,r23,r24,r25,r26,r27,r28,r29,rar,tar,tgz,z,zip"),
            new("doc","asm,c,cc,chm,cpp,csv,cxx,doc,docm,docx,dot,dotm,dotx,efu,epub,h,hpp,htm,html,hxx,ini,java,js,json,lua,md,mht,mhtml,mobi,odp,ods,odt,ofd,pdf,php,pl,potm,potx,ppam,pps,ppsm,ppsx,ppt,pptm,pptx,ps1xml,pssc,pub,py,rtf,sldm,sldx,sql,tsv,txt,vb,vsd,wpd,wps,wri,xlam,xls,xlsb,xlsm,xlsx,xltm,xltx,xml,xsl"),
            new("exe", "bat,cmd,exe,msi,msp,msu,ps1,scr,msix,vbs"),
            new("pic", "ani,apng,avif,avifs,bmp,bpg,cur,dds,gif,heic,heics,heif,heifs,hif,ico,jfi,jfif,jif,jpe,jpeg,jpg,jxl,jxr,pcx,png,psb,psd,svg,tga,tif,tiff,wdp,webp,wmf,wbmp,icl,jp2,mpng,raw,nef,hdp") ,
            new("video", "3g2,3gp,3gp2,3gpp,amr,amv,asf,asx,avi,bdmv,bik,d2v,divx,drc,dsa,dsm,dss,dsv,evo,f4v,flc,fli,flic,flv,hdmov,ifo,ivf,m1v,m2p,m2t,m2ts,m2v,m4v,mkv,mod,mov,mp2v,mp4,mp4v,mpe,mpeg,mpg,mpls,mpv2,mpv4,mts,ogm,ogv,ogx,pss,pva,qt,ram,ratdvd,rm,rmm,rmvb,roq,rpm,smil,smk,swf,tod,tp,tpr,ts,tts,uvu,vob,vp6,webm,wm,wmp,wmv,wmx,wvx"),
            new("web", "html,htm,css,js,svg,json,xml,scss,sass,less,styl,ts,mjs,vue,tsx,jsx"),
            new("font", "ttf,otf,woff,woff2,ttc,ttf"),
        ];

        internal int MaxSearchCount
        {
            get => int.TryParse(_maxSearchCount.Value, out var maxSearchCount) ? maxSearchCount : DefaultMaxSearchCount;
            set
            {
                _maxSearchCount.Value = value.ToString(CultureInfo.InvariantCulture);
                SaveSettings();
            }
        }

        internal bool MacroEnabled => _macroEnabled.Value;

        internal bool MatchCase => _matchCase.Value;
        internal bool MatchDiacritics => _matchDiacritics.Value;

        internal bool MatchWholeWords => _matchWholeWords.Value;

        internal bool MatchPath => _matchPath.Value;

        internal bool MatchPrefix => _matchPrefix.Value;

        internal bool MatchSuffix => _matchSuffix.Value;

        internal bool IgnorePunctuation => _ignorePunctuation.Value;

        internal bool IgnoreWhitespace => _ignoreWhitespace.Value;

        internal uint? SearchFoldersFirst
        {
            get
            {
                if (_searchFoldersFirst.Value is null || string.IsNullOrEmpty(_searchFoldersFirst.Value))
                {
                    return default;
                }

                var success = uint.TryParse(_searchFoldersFirst.Value, out var result);

                if (!success)
                {
                    return default;
                }

                return result;
            }
        }

        /// <summary>每次搜索自动加到查询最前面的 Everything 语法（如 ext:pdf）。</summary>
        internal string QueryPrefix => _queryPrefix.Value?.Trim() ?? string.Empty;

        /// <summary>正则搜索总开关（与查询里的 @ 前缀叠加）。</summary>
        internal bool UseRegex => _useRegex.Value;

        /// <summary>
        /// 排序令牌（Everything 查询语法的 sort: 值，如 date-modified）。
        /// default 或空 = 不干预，用 Everything 自身的顺序。
        /// </summary>
        internal string? SortByToken
        {
            get
            {
                var value = _sortBy.Value;
                return string.IsNullOrEmpty(value) || string.Equals(value, "default", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : value;
            }
        }

        /// <summary>「发送到指定程序」的程序完整路径；空 = 不显示该菜单项。</summary>
        internal string SendToProgram => _sendToProgram.Value?.Trim() ?? string.Empty;

        /// <summary>预览的快捷键；设置里的文本解析不了时返回 null（不绑定）。</summary>
        internal KeyChord? PreviewShortcut => HotkeyParser.TryParse(_previewHotkey.Value);

        /// <summary>
        /// 把全部设置项按原顺序加进目标容器（顺序即设置表单里的显示顺序）。
        ///
        /// 用途：宿主（CmdPal「设置 → 扩展」）只认识聚合 Provider
        /// <see cref="PowerEverythingProvider"/>，两个子 Provider 自己设的 Settings 宿主
        /// 根本看不到 —— 那份宿主表单此前是空白的。由聚合 Provider 建一个合并容器，
        /// 再把两边设置项加进去。<see cref="Settings.Add{T}"/> 只是按 Key 存字典，
        /// 同一个设置项同时留在原容器里没有任何副作用。
        /// </summary>
        internal void AddTo(Microsoft.CommandPalette.Extensions.Toolkit.Settings target)
        {
            // 与构造函数里的添加顺序保持一致（顺序即表单显示顺序）。
            target.Add(_maxSearchCount);
            target.Add(_sortBy);
            target.Add(_searchFoldersFirst);
            target.Add(_queryPrefix);
            target.Add(_useRegex);
            target.Add(_macroEnabled);
            target.Add(_previewHotkey);
            target.Add(_sendToProgram);
            target.Add(_matchCase);
            target.Add(_matchWholeWords);
            target.Add(_matchPath);
            target.Add(_matchPrefix);
            target.Add(_matchSuffix);
            target.Add(_matchDiacritics);
            target.Add(_ignorePunctuation);
            target.Add(_ignoreWhitespace);
        }
    }
}