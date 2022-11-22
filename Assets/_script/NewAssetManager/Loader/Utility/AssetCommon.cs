namespace FunPlus.AssetManagement
{
    public delegate bool CheckCache(string abPath);
    
    public enum AssetStatus
    {
        Succeed,
        NotExist,
        WaitDownload,
    }

    public enum AssetLoadPriority
    {
        Priority_Sync = 0, //使用同步加载
        Priority_Fast = 100,//使用同步加载，不超过1帧加载数量时，下帧返回，大于它都将使用异步
        Priority_Common = 1000
    }
    
    public enum AssetTypeEx
    {
        Asset,
        AssetBundle,
    }
}