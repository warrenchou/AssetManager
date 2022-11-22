
using System.Collections.Generic;
namespace Funplus.AssetManagement
{
    public class AssetBundleMap
    {
        // 是否强制名字小写
        static bool forceLower = true;
        private struct AssetPathInfo
        {
            public string abPath;
            public string assetFullPath;
        }

        static private Dictionary<string, AssetPathInfo> assetMap = new Dictionary<string, AssetPathInfo>();
        
        public string GetBundle(string str)
        {
            string path = forceLower ? str.ToLower() : str;
            if (!assetMap.TryGetValue(path, out var info))
            {
                return null;
            }
            return info.abPath;
        }

        public bool GetABPath(string assetPath, out string abPath, out string assetName)
        {
            string path = forceLower ? assetPath.ToLower() : assetPath;
            if (System.IO.Path.HasExtension(path))
            {
                path = System.IO.Path.ChangeExtension(path, null);
            }
            if (!assetMap.TryGetValue(path, out var info))
            {
                abPath = null;
                assetName = null;
                return false;
            }

            abPath = info.abPath;
            assetName = info.assetFullPath;
            return true;
        }
        
    }
}