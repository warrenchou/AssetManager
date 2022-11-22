namespace FunPlus.AssetManagement
{
    using Object = UnityEngine.Object;
    public class AssetHandle
    {
        public static AssetHandle invalid = new AssetHandle(null);
        public static AssetHandle waitDownload = new AssetHandle(null,AssetStatus.WaitDownload);
        public AssetRef assetRef { get; }
        
        public Object asset
        {
            get
            {
                if (!assetRef.isValid)
                {
                    return null;
                }

                return assetRef.asset;
            }
        }
        
        public bool isValid => assetRef != null && assetRef.isValid && asset != null;

        
        public AssetStatus status { get;  set; }


        public AssetHandle(AssetRef assetRef)
        {
            this.assetRef = assetRef;
            if (assetRef != null)
            {
                status = assetRef.asset == null ? AssetStatus.NotExist : AssetStatus.Succeed;
            }
            else
            {
                status = AssetStatus.NotExist;
            }
        }

        public AssetHandle(AssetRef assetRef, AssetStatus status)
        {
            this.assetRef = assetRef;
            this.status = status;
        }
        
        public void RefAsset()
        {
            if (isValid)
            {
                assetRef.Ref();
            }
        }
        
        public int UnRefAsset()
        {
            if (isValid)
            {
                assetRef.UnRef();
                return assetRef.refCount;
            }
            else
            {
                return 0;
            }
        }
    }
}