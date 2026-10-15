using EverythingExtension.Internal;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EverythingExtension
{
    internal interface IEverythingClient : IDisposable
    {
        #region Public Methods

        public long Cookie { get; }
        public ConcurrentQueue<SearchResult> SearchResults { get; }
        Version Version { get; }

        /// <summary>
        /// 限定搜索范围到某个文件夹（及其子文件夹）。设置后，每次查询会自动在关键字前加上
        /// <c>path:"folder\*"</c> 过滤条件。用于「在打开的文件夹中搜索」功能。
        /// </summary>
        string? Scope { get; set; }

        void EnsureEverythingAvailable();

        void Cancel();

        bool Initialize();

        void Execute(string searchText, long cookie);

        uint IncRunCountFromFilename(string filename);

        #endregion Public Methods
    }
}