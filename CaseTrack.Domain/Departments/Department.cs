namespace CaseTrack.Domain.Departments;

// 承辦單位。主鍵用代理鍵（Guid），代碼另外加唯一索引：代碼要改的時候，參照它的資料（案件）不用跟著改
public sealed class Department
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; }
    public string Name { get; private set; }

    public Department(string code, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Code = code;
        Name = name;
    }
}
