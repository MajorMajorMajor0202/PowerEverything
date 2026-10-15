using EverythingExtension.Exceptions;
using EverythingExtension.Internal;
using EverythingExtension.Pages;
using EverythingExtension.Properties;

using Microsoft.CommandPalette.Extensions.Toolkit;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EverythingExtension
{
    internal sealed partial class FallbackEverythingItem : FallbackCommandItem
    {
        #region Fields

        // 同样必须走 fork 自己的命名空间，否则会与上游包的全局搜索 fallback 撞车，
        // 表现为搜索结果里出现两条一模一样的「使用 Everything 搜索 "xxx"」。
        private const string CommandId = CommandIds.Fallback;
        private static readonly NoOpCommand BaseCommandWithId = new() { Id = CommandId };
        private readonly CompositeFormat _fallbackItemSearchPageTitleFormat = CompositeFormat.Parse(Resources.everything_search_format);
        private readonly EverythingExtensionPage _page;

        #endregion Fields

        #region Public Constructors

        public FallbackEverythingItem(EverythingExtensionPage page) : base(BaseCommandWithId, Resources.everything_find_fallback_display_title, CommandId)
        {
            _page = page;
            Title = string.Empty;
            Subtitle = Resources.everything_fallback_multipleResults_subtitle;
            Icon = IconHelpers.FromRelativePath("Assets\\Images\\search.png");
           
        }

        public override void UpdateQuery(string query)
        {
            Title = string.Empty;
            Subtitle = string.Empty;
            Icon = null;

            _page.EverythingInitialize();

            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            //_page.UpdateSearchText("", query);
            _page.SearchText = query;
            Title = string.Format(CultureInfo.CurrentCulture, _fallbackItemSearchPageTitleFormat, query);
            Subtitle = Resources.everything_fallback_multipleResults_subtitle;
            Icon = IconHelpers.FromRelativePath("Assets\\Images\\search.png");
            Command = _page;
            OnPropertyChanged(nameof(Title));
        }

        #endregion Public Constructors
    }
}