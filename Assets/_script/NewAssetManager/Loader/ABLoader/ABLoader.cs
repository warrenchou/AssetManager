using System;
using System.Collections.Generic;
using System.IO;
using Funplus.AssetManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FunPlus.AssetManagement
{
    public class ABLoader
    {
        public static bool OUTPUT_LOG =
#if UNITY_EDITOR && AB_ASSET
            true;
#else
        false;
#endif
        
#region GCPOOL
        private class ABInfo : IGCPool
        {
            //assetBundle
            public AssetBundle ab;
            
            //todo:加载时，依赖的内容
            public AssetRefList refList;
            
            public int loadedFrameCount;
            public int unusedFrame;
            
  
            public Dictionary<string, Object> loadedAssets;
            public void Reset()
            {
                ab = null;
                loadedFrameCount = 0;
                unusedFrame = 0;

                refList?.ClearRef();
                loadedAssets?.Clear();
                
            }
        }
        
        private class InternalLoadABRequest : IGCPool
        {
            public string abPath { get; private set;}
            public ABInfo loadingInfo { get; private set;}
            public AssetBundleCreateRequest asyncCreateRequest;
            public bool isDone { get; private set; } = false;

            private int refCount;
            private static GCPool<InternalLoadABRequest> internalAsyncReqPool = new GCPool<InternalLoadABRequest>();
            
            private DelegateList<InternalLoadABRequest> _delegate = new DelegateList<InternalLoadABRequest>();

            public event Action<InternalLoadABRequest> onCompleted
            {
                add
                {
                    if (isDone)
                    {
                        value.Invoke(this);
                    }
                    else
                    {
                        _delegate.Add(value);
                    }
                }
                
                remove => _delegate.Remove(value);
            }

            public void Reset()
            {
                abPath = null;
                loadingInfo = null;
                _delegate.Clear();
                asyncCreateRequest = null;
                isDone = false;
                refCount = 0;
            }
            
            public void TriggerSyncLoad()
            {
                var ab = asyncCreateRequest.assetBundle;
                
                //complete没有被触发，手动触发
                if (refCount > 0 && !isDone)
                {
                    Completed(ab);
                }
            }
            
            public void Completed(AssetBundle ab)
            {
                isDone = true;
                loadingInfo.ab = ab;
                if (ab == null)
                {
                    Debug.LogError($"load asset bundle failed:{abPath}");
                }
                _delegate.Invoke(this);
                _delegate.Clear();
            }
            
            public void Release()
            {
                refCount--;
                if (refCount <= 0)
                {
                    internalAsyncReqPool.Free(this);
                }
            }
            
            public void Ref()
            {
                refCount++;
            }
            
            public static InternalLoadABRequest Get(string abPath,ABInfo abInfo)
            {
                var req = internalAsyncReqPool.Get();
                req.abPath = abPath;
                req.loadingInfo = abInfo;
                return req;
            }
        }


        private class BatchLoadRequest : IGCPool
        {
            private List<InternalLoadABRequest> loadRequests = new List<InternalLoadABRequest>();
            private List<InternalLoadABRequest> finishRequests = new List<InternalLoadABRequest>();
            
            private Action onCompleted;
            
            private bool isStart = false;
            public void Reset()
            {
                isStart = false;
                onCompleted = null;
                loadRequests.Clear();
                finishRequests.Clear();
            }

            public void WaitAll(Action completed)
            {
                isStart = true;
                onCompleted = completed;
                CheckFinish();
            }

            public void Add(InternalLoadABRequest req)
            {
                if (loadRequests.Contains(req))
                {
                    return;
                }
                req.Ref();
                loadRequests.Add(req);
                req.onCompleted += OnRequestLoaded;
            }
            
            private void OnRequestLoaded(InternalLoadABRequest req)
            {
                finishRequests.Add(req);
                if (isStart)
                {
                    CheckFinish();
                }
            }
            
            private void CheckFinish()
            {
                if (finishRequests.Count == loadRequests.Count)
                {
                    onCompleted?.Invoke();
                }
            }
            
            public void BatchRelease()
            {
                foreach (var req in finishRequests)
                {
                    req.Release();
                }
            }
            
        }

        class LoadSceneRequest : IGCPool
        {
            public void Reset()
            {
               
            }
        }
        

       
        
        
#endregion
     
        // 资源根路径
        public string rootPath { get; private set; }
        // 名称
        public string LoaderName { get; private set; }
        //资源路径
        public string patchFolderPath { get; set; }
        
        // 已经loading完的
        private Dictionary<string, ABInfo> loadedAbs = new Dictionary<string, ABInfo>();
        private GCPool<ABInfo> abInfoPool = new GCPool<ABInfo>();
        
       
        // 正在加载的req
        private Dictionary<string, InternalLoadABRequest> loadingAbs =
            new Dictionary<string, InternalLoadABRequest>();
        
        // 正在使用的
        private Dictionary<string,int> _usingAbs = new Dictionary<string, int>();
        
        private GCPool<BatchLoadRequest> batchPool = new GCPool<BatchLoadRequest>();
        
        //无引用时的缓存
        private LRUCache lruCache;
        private int cacheSize;
        
        private AssetRefManager assetRefMgr;
        private AssetRefManager bundleRefMgr;
        
        private AssetBundleMap bundleMap = new AssetBundleMap();
        
        
        // 判断是否要缓存，比如场景不需要缓存
        public CheckCache checkCache { get; set; }
        
#if UNITY_EDITOR
        private bool released = false;
#endif
        // 加载asset to bundle的表
        public bool LoadAsset2AB()
        {
            return true;
        }
        
        // 加载依赖关系
        public bool LoadManifest()
        {
            return true;
        }
        
        // 获取依赖
        private HashSet<string> GetAllDependencies(string abPath)
        {
            HashSet<string> depSet = new HashSet<string>();
            
            return depSet;
        }
        
        public bool Init(string path,int lruSize)
        {
            _Init(path,lruSize);
            LoaderName = Path.GetFileName(rootPath);
            //加载ab映射关系
            LoadAsset2AB();
            // 加载依赖关系
            LoadManifest();
            return true;
        }
        
        private void _Init(string path, int lruSize)
        {
            released = false;
            loadedAbs.Clear();
            usingAbs.Clear();
            SetCacheSize(lruSize);
            assetRefMgr = new AssetRefManager("AssetRefManager");
            bundleRefMgr = new AssetRefManager("BundleRefManager");
            this.rootPath = path;
        }
        
        public void SetCacheSize(int cacheSize)
        {
            this.cacheSize = cacheSize;
            lruCache = new LRUCache(cacheSize);
        }
        
        public void Release()
        {
            released = true;
            usingAbs.Clear();
            assetRefMgr.Clear();
            bundleRefMgr.Clear();
            UnloadUnusedTotal();
            
            
            loadedAbs?.Clear();
            abPatchFullPathDic?.Clear();
            abPatchFullPathDic?.Clear();
        }

        public bool TryGetloaded(string abPath, string assetName, out AssetHandle assetHandle)
        {
            assetHandle = AssetHandle.invalid;
            if (!loadedAbs.TryGetValue(abPath, out var info))
            {
                return false;
            }

            if (string.IsNullOrEmpty(assetName))
            {
                assetHandle = new AssetHandle(bundleRefMgr.GetOrCreateRef(info.ab));
                return true;
            }

            if (info.loadedAssets == null || info.loadedAssets.Count < 1)
            {
                return false;
            }

            if (info.loadedAssets.TryGetValue(assetName, out var asset))
            {
                if (ShouldCache(abPath))
                {
                    lruCache.Put(abPath);
                }

                assetHandle = new AssetHandle(CreateAssetRef(assetName, asset,info.ab));
                return true;
            }

            return false;
        }

        public List<AssetHandle> LoadAllShaders()
        {
            List<AssetHandle> handleList = new List<AssetHandle>();
            return handleList;
        }

        private void OnAssetLoaded(AssetLoadRequest req,Object asset,AssetBundle ab,string abPath,string assetName)
        {
            if (asset != null)
            {
                var handle = new AssetHandle(CreateAssetRef(assetName, asset, ab));
                req._assetHandle = handle;
            }
            else
            {
                if (OUTPUT_LOG)
                {
                    Debug.LogWarning("load failed:" + abPath + "/" + assetName);
                    if (!ab.Contains(assetName))
                    {
                        Debug.LogError("ab not contains asset." + assetName);
                    }
                }
            }
            req.InternalLoaded();
        }
        
        // 异步加载
        public bool LoadFromABAsync(string abPath,string assetName,AssetLoadRequest req)
        {
            // 检查资源情况，给边玩边下流的接口
            var checkAB = CheckAbStatus(abPath, assetName, req);
            if (!checkAB)
            {
                req.InternalLoaded();
                return false;
            }
            
            LoadAB(abPath, req.priority,(info) =>
            {
                _LoadFromAbAsync(abPath, info.ab, assetName, req);
            });

            return true;
        }

        private void _LoadFromAbAsync(string abPath, AssetBundle ab, string assetName, AssetLoadRequest req)
        {
            if (ab == null)
            {
                req.InternalLoaded();
                return;
            }

            if (string.IsNullOrEmpty(assetName))
            {
                req._assetHandle = new AssetHandle(bundleRefMgr.GetOrCreateRef(ab));
                req.InternalLoaded();
            }

            if (req.priority <= (int)AssetLoadPriority.Priority_Sync)
            {
                var asset = req._type == null
                    ? ab.LoadAsset(assetName)
                    : ab.LoadAsset(assetName,req._type);
                OnAssetLoaded(req, asset, ab, abPath, assetName);
            }
            else
            {
                var loadReq = req._type == null ? ab.LoadAssetAsync(assetName)
                    : ab.LoadAssetAsync(assetName, req._type);
                if (loadReq == null)
                {
                    Debug.LogError("load from ab failed." + abPath + "," + assetName);
                    req.InternalLoaded();
                    return;
                }
                AddUsing(abPath);
                loadReq.priority = req.priority;
                loadReq.completed += (opt) =>
                {
                    var asset = loadReq.asset;
                    OnAssetLoaded(req,asset, ab,abPath,assetName);
                    RemoveUsing(abPath);
                };
            }
        }

        void LoadAB(string abPath, int priority, Action<ABInfo> complete)
        {
            var batch = batchPool.Get();

            AddUsing(abPath);
            var req = DoLoadAB(abPath, priority, 0);
            batch.Add(req);

            var deps = GetAllDependencies(abPath);

            void OnDepLoaded(InternalLoadABRequest depLoadReq)
            {
                if (depLoadReq.loadingInfo.ab == null)
                {
                    return;
                }

                if (req.loadingInfo.refList == null)
                {
                    req.loadingInfo.refList = new AssetRefList();
                }
                
                req.loadingInfo.refList?.AddRef(bundleRefMgr.GetOrCreateRef(depLoadReq.loadingInfo.ab));
            }

            foreach (var dep in deps)
            {
                AddUsing(dep);
                var depReq = DoLoadAB(dep, priority, 1);
                depReq.onCompleted+=OnDepLoaded;
                batch.Add(depReq);
            }
            
            batch.WaitAll(() =>
                {
                    RemoveUsing(abPath);
                    foreach (var dep in deps)
                    {
                        RemoveUsing(dep);
                    }
                    
                    batch.BatchRelease();
                    batchPool.Free(batch);
                    loadedAbs.TryGetValue(abPath, out var info);
                    complete?.Invoke(info);
                }
                );
        }

        InternalLoadABRequest DoLoadAB(string abPath, int priority, int lv)
        {
            if (ShouldCache(abPath))
            {
                lruCache.Put(abPath);
            }

            if (loadingAbs.TryGetValue(abPath, out var loadingReq))
            {
                if(priority <= (int)AssetLoadPriority.Priority_Sync)
                {
                    //发生了同步
                    //直接触发同步加载，这时这个ab会加入loadedAbs，同时loadingReq会自动release掉
                    //这里不能返回loadingReq，因为他已经被之前添加任务时，设置的回调释放掉了
                    //直接让后续已经加载完的ab去判断，返回一个拷贝的loadedReq即可
                    loadingReq.TriggerSyncLoad();
                }
                else
                {
                    return loadingReq;
                }
            }

            if (loadedAbs.TryGetValue(abPath, out var loadedInfo))
            {
                if (loadedInfo.ab != null)
                {
                    loadedInfo.unusedFrame = 0;
                    var loadedReq = InternalLoadABRequest.Get(abPath, loadedInfo);
                    loadedReq.Completed(loadedInfo.ab);
                    return loadedReq;
                }
            }
            
            var internalReq = DoAsyncLoadAB(abPath, priority);
            loadingAbs.Add(abPath,internalReq);

            if (lv == 0)
            {
                if (OUTPUT_LOG)
                {
                    if (priority <= 0)
                    {
                        Debug.LogFormat("<color='green'>Load Ab <color='red'>Sync</color>:{0}.{1}</color>", abPath, Time.frameCount);
                    }
                    else
                    {
                        Debug.LogFormat("<color='green'>Load Ab <color='yellow'>Async</color>:{0}.{1}</color>", abPath, Time.frameCount);
                    }
                }
            }
            else
            {
                if (OUTPUT_LOG)
                {
                    string levelStr = new string('-', lv * 4);
                    if (priority <= 0)
                    {
                        Debug.LogFormat("<color='green'>-{0}>Load dep Ab <color='red'>Sync</color>:{1}.{2}</color>", levelStr, abPath , Time.frameCount);
                    }
                    else
                    {
                        Debug.LogFormat("<color='green'>-{0}>Load dep Ab <color='yellow'>Async</color>:{1}.{2}</color>", levelStr, abPath, Time.frameCount);
                    }
                }
            }
            
            internalReq.onCompleted += (reqRet) =>
            {
                reqRet.loadingInfo.loadedFrameCount = Time.frameCount;
                loadingAbs.Remove(reqRet.abPath);
                loadedAbs.Add(reqRet.abPath,reqRet.loadingInfo);
            };
            return internalReq;
        }

        InternalLoadABRequest DoAsyncLoadAB(string abPath, int priority)
        {
            ABInfo info = abInfoPool.Get();
            string fullPath = GetPatchFullPath(abPath);

            var req = InternalLoadABRequest.Get(abPath, info);

            void OnLoaded(AsyncOperation opt)
            {
                AssetBundleCreateRequest cr = opt as AssetBundleCreateRequest;
                req.Completed(cr.assetBundle);
            }

            try
            {
                if (priority <= (int)AssetLoadPriority.Priority_Sync)
                {
                    AssetBundle ab = AssetBundle.LoadFromFile(fullPath);
                    req.Completed(ab);
                }
                else
                {
                    req.asyncCreateRequest = AssetBundle.LoadFromFileAsync(fullPath);
                    //操作完成时调用的事件。即使操作能够同步完成，也将在下一帧调用在创建它的调用所在的帧中注册的事件处理程序。
                    //如果处理程序是在操作完成后注册的，并且已调用 complete 事件，则将同步调用该处理程序。
                    req.asyncCreateRequest.completed += OnLoaded;
                    req.asyncCreateRequest.priority = priority;
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                req.Completed(null);
                
            }


            return req;
        }
        
        private void AddUsing(string abPath)
        {
            if (_usingAbs.TryGetValue(abPath, out var val))
            {
                _usingAbs[abPath] = val + 1;
            }
        }
        
        private void RemoveUsing(string abPath)
        {
            if (_usingAbs.TryGetValue(abPath, out var val))
            {
                val--;
                if (val <= 0)
                {
                    _usingAbs.Remove(abPath);
                }
                else
                {
                    _usingAbs[abPath] = val;
                }
            }
        }
       

        BundleDownloadManager bundleDownloadManager;
        bool CheckAbStatus(string assetPath, string abPath, AssetLoadRequest req)
        {
            if (bundleDownloadManager == null)
            {
                return true;
            }

            var status = bundleDownloadManager.CheckAssetLoadable(assetPath, abPath);
            if (status == AssetStatus.Succeed)
            {
                return true;
            }
            if (status == AssetStatus.NotExist)
            {
                Debug.LogError($"{abPath} check ab status:{status},load failed");
            }
            
            req._assetHandle = status == AssetStatus.WaitDownload ? AssetHandle.waitDownload : AssetHandle.invalid;
            return false;
        }
        
        private List<string> tmps = new List<string>();
        private Dictionary<string,int> usingAbs = new Dictionary<string, int>();
        
       
        private bool HasBundleRef(ABInfo info)
        {
            if (info.ab == null)
            {
                return false;
            }
            //这个ab还有依赖引用，不能释放
            if (bundleRefMgr.HasRef(info.ab))
            {
                return true;
            }

            return false;
        }
        
        
        public bool UnloadUnusedStep(int maxUnload = -1)
        {
            tmps.Clear();
            bool isReachMaxUnload = false;
            foreach (var pair in loadedAbs)
            {
                var info = pair.Value;
                if (usingAbs.ContainsKey(pair.Key))
                {
                    continue;
                }
                if (Time.frameCount - info.loadedFrameCount <= 300) //不能立即删除，yield模式需要至少等1帧，才能正常引用
                {
                    continue;
                }
                if (!HasBundleRef(info))
                {
                    tmps.Add(pair.Key);
                }
                if (maxUnload > 0 && tmps.Count >= maxUnload)
                {
                    isReachMaxUnload = true;
                    break;
                }
            }
            bool cleard = tmps.Count == 0;
            foreach (var key in tmps)
            {
                //_UnloadAb(key);
            }
            return cleard && !isReachMaxUnload;
        }
        
        public void UnloadUnusedTotal()
        {
            int step = 0;
            while (!UnloadUnusedStep())
            {
                step++;
                if (step >= 100)
                {
                    Debug.LogError("too many unload unued step:" + step);
                    break;
                }
            }
        }
        
        private Dictionary<string, string> abPatchFullPathDic;
        public Func<string, string,string> getAbMd5Path;
        public string GetPatchFullPath(string abPath)
        {
            if (abPatchFullPathDic == null)
            {
                abPatchFullPathDic = new Dictionary<string, string>();
            }
            
            if (abPatchFullPathDic.TryGetValue(abPath,out var fullPath))
            {
                return fullPath;
            }

            var loadAbPath = abPath;
            if (getAbMd5Path != null)
            {
                loadAbPath = getAbMd5Path(abPath, LoaderName);
            }
            fullPath = Path.Combine(patchFolderPath, loadAbPath);

            abPatchFullPathDic[abPath] = fullPath;
            return fullPath;
        }
        
        private bool ShouldCache(string abPath)
        {
            if (checkCache == null)
            {
                return true;
            }
            return checkCache(abPath);
        }

        AssetRef CreateAssetRef(string assetName, Object asset, AssetBundle ab)
        {
            var assetRef = assetRefMgr.GetOrCreateRef(asset);
            assetRef.linkRef = bundleRefMgr.GetOrCreateRef(ab);
            if (loadedAbs.TryGetValue(ab.name, out var info))
            {
                if (info.loadedAssets == null)
                {
                    info.loadedAssets = new Dictionary<string, Object>();
                }

                info.loadedAssets[assetName] = asset;
            }

            return assetRef;
        }
        
        
        public bool GetABPath(string assetPath, out string abPath, out string assetName)
        {
            return bundleMap.GetABPath(assetPath, out abPath, out assetName);
        }
        
    }
}