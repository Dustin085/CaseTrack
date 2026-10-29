namespace CaseTrack.Domain.Departments;

// 承辦單位。主鍵用代理鍵（Guid），代碼另外加唯一索引：代碼要改的時候，參照它的資料（案件）不用跟著改
public sealed class Department
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; }
    public string Name { get; private set; }
    // 停用不刪除：舊案件還指向這個單位，名稱要能繼續顯示；停用後只是不能再分派新案件
    public bool IsActive { get; private set; } = true;

    public Department(string code, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Code = code;
        Name = name;
    }

    // 只改名稱：代碼是對外的識別（其他系統、報表可能用它對照），建立後不能改
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    // 停用時不檢查有沒有處理中的案件：單位裁併時，已分派的案件照樣由原單位處理完，只是不再分派新案件
    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
