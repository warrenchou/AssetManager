using System.Collections.Generic;

namespace FunPlus.AssetManagement
{
    using Object = UnityEngine.Object;
    using Debug = UnityEngine.Debug;
    
    public class AssetRef : IGCPool
    {
        public Object asset { get; private set; }
        // 计数
        public int refCount { get; private set; }
        
        public AssetRefManager manager { get; internal set; }
        
        public AssetRef linkRef { get; set; }
        public bool isValid => manager != null;
        
        // 创建
        public void Create(Object assetObj, AssetRefManager mgr)
        {
            asset = assetObj;
            manager = mgr;
        }

        // 释放
        public void Release()
        {
            refCount = 0;
            linkRef = null;
            manager = null;
            asset = null;
        }

        public void Ref()
        {
            refCount++;
            if (refCount == 1)
            {
                linkRef?.Ref();
            }
#if UNITY_EDITOR
            refCalledStacks.Add(GetCallInfo());
#endif
        }

        public void UnRef()
        {
            refCount--;
            if (refCount == 0)
            {
                linkRef?.UnRef();
            }

            if (refCount < 0)
            {
                Debug.LogError("AssetRef:refCount < 0");
                refCount = 0;
            }
#if UNITY_EDITOR
            unRefCalledStacks.Add(GetCallInfo());
#endif
        }
        
        public bool HasRef()
        {
            return isValid && refCount > 0;
        }
        
        public void Reset()
        {
            refCount = 0;
            linkRef = null;
            manager = null;
            asset = null;
#if UNITY_EDITOR
            refCalledStacks.Clear();
            unRefCalledStacks.Clear();
#endif
        }
        
#if UNITY_EDITOR
        public List<string> refCalledStacks = new List<string>();
        public List<string> unRefCalledStacks = new List<string>();
        
        public string GetCallInfo()
        {
            string msg = UnityEngine.StackTraceUtility.ExtractStackTrace();
            string[] lines = msg.Split('\n');
            string trimMsg = "";
            for(int i=2; i<lines.Length; i++)
            {
                trimMsg += lines[i] + '\n';
            }
            return trimMsg;
        }
        //清空引用
        internal void ReleaseAtEditor()
        {
            refCount = 0;
            manager = null;
        }
#endif
    }

    public class AssetRefList
    {
        List<AssetRef> refList = new List<AssetRef>();
        
        public int Count => refList.Count;

        public void AddRef(AssetHandle assetHandle)
        {
            if (assetHandle.isValid)
            {
                AddRef(assetHandle.assetRef);
            }
        }
        
        public void RemoveRef(AssetHandle assetHandle)
        {
            if (assetHandle.isValid)
            {
                RemoveRef(assetHandle.assetRef);
            }
        }
        
        public void AddRef(AssetRef assetRef)
        {
            if (assetRef == null)
            {
                return;
            }
            if (!assetRef.isValid)
            {
                return;
            }
            if (!refList.Contains(assetRef))
            {
                assetRef.Ref();
                refList.Add(assetRef);
            }
        }
        
        public bool RemoveRef(AssetRef assetRef)
        {
            bool ret = refList.Remove(assetRef);
            if (ret && assetRef.isValid)
            {
                assetRef.UnRef();
            }
            return ret;
        }
        
        public void ClearRef()
        {
            foreach (var assetRef in refList)
            {
                if (assetRef.isValid)
                {
                    assetRef.UnRef();
                }
            }
            refList.Clear();
        }

        public bool Contains(AssetRef assetRef)
        {
            return refList.Contains(assetRef);
        }

    }
    
    public class AssetRefManager
    {
        public readonly string name;

        private Dictionary<int, AssetRef> assetRefs = new Dictionary<int, AssetRef>();

        private GCPool<AssetRef> assetRefPool = new GCPool<AssetRef>();
        public AssetRefManager(string name)
        {
            this.name = name;
        }

        public void Clear()
        {
#if UNITY_EDITOR
            foreach (var pair in assetRefs)
            {
                pair.Value.ReleaseAtEditor();
            }
#endif
            assetRefs.Clear();
        }
        
        int GetRefId(Object asset)
        {
            return asset.GetInstanceID();
        }

        public AssetRef GetRef(Object asset)
        {
            if (asset == null)
            {
                return null;
            }

            return GetRef(GetRefId(asset));
        }
        
        public AssetRef GetRef(int key)
        {
            AssetRef assetRef;
            if (assetRefs.TryGetValue(key, out assetRef))
            {
                return assetRef;
            }
            return null;
        }

        // 销毁
        public void DestroyRef(Object asset)
        {
            if (asset == null)
            {
                Debug.LogWarning("AssetRefManager.DestroyRef asset is null");
                return;
            }

            var key = GetRefId(asset);
            var assetRef = GetRef(key);
            if (assetRef != null)
            {
                assetRefs.Remove(key);
                assetRef.Release();
                assetRefPool.Free(assetRef);
            }
        }

        /// <summary>
        /// 使用InstanceID管理对象，用于对象的引用管理
        /// </summary>
        /// <param name="asset"></param>
        /// <returns></returns>
        public AssetRef GetOrCreateRef(Object asset)
        {
            if (asset == null)
            {
                Debug.LogWarning("AssetRefManager.GetOrCreateRef asset is null");
                return null;
            }
            
            int key = GetRefId(asset);
            AssetRef assetRef;
            if (assetRefs.TryGetValue(key, out assetRef))
            {
                return assetRef;
            }
            else
            {
                assetRef = assetRefPool.Get();
                assetRef.Create(asset,this);
                assetRefs.Add(key, assetRef);
                return assetRef;
            }
        }

        public bool HasRef(Object asset)
        {
            if (asset == null)
            {
                Debug.LogWarning("AssetRefManager.HasRef asset is null");
                return false;
            }
            
            AssetRef assetRef;
            if (!assetRefs.TryGetValue(GetRefId(asset), out assetRef))
            {
                return false;
            }
            return assetRef.HasRef();
            
        }
        
    }
}