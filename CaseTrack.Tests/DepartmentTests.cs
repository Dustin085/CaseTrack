using CaseTrack.Domain.Departments;

namespace CaseTrack.Tests;

public class DepartmentTests
{
    [Fact]
    public void Constructor_WhenCreated_HasGeneratedId()
    {
        var department = new Department("PWD", "工務局");

        Assert.NotEqual(Guid.Empty, department.Id);
        Assert.Equal("PWD", department.Code);
        Assert.Equal("工務局", department.Name);
    }

    [Theory]
    [InlineData("", "工務局")]
    [InlineData("  ", "工務局")]
    [InlineData("PWD", "")]
    [InlineData("PWD", "  ")]
    public void Constructor_WithBlankArgument_ThrowsArgumentException(string code, string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Department(code, name));
    }
}
