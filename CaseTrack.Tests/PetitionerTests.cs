using CaseTrack.Domain.Cases;

namespace CaseTrack.Tests;

public class PetitionerTests
{
    [Fact]
    public void CreatePetitioner_WithNameAndPhone_PetitionerCreated()
    {
        var petitioner = new Petitioner("Tester", "0987654321", null, null);

        Assert.Equal("Tester", petitioner.Name);
        Assert.Equal("0987654321", petitioner.Phone);
    }

    [Fact]
    public void CreatePetitioner_WithNameAndEmail_PetitionerCreated()
    {
        var petitioner = new Petitioner("Tester", null, "test@test.com", null);

        Assert.Equal("Tester", petitioner.Name);
        Assert.Equal("test@test.com", petitioner.Email);
    }

    [Fact]
    public void CreatePetitioner_WithoutContact_ThrowsInvalidPetitionerException()
    {
        Assert.Throws<InvalidPetitionerException>(() =>
            new Petitioner("Tester", null, null, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreatePetitioner_BlankName_ThrowsInvalidPetitionerException(string? name)
    {
        Assert.Throws<InvalidPetitionerException>(() =>
            new Petitioner(name, "0987654321", null, null));
    }

    // 電話、Email 都是空白：跟都沒填一樣，沒有任何聯絡方式
    [Theory]
    [InlineData("", "")]
    [InlineData("   ", null)]
    [InlineData(null, "   ")]
    public void CreatePetitioner_BlankPhoneAndEmail_ThrowsInvalidPetitionerException(string? phone, string? email)
    {
        Assert.Throws<InvalidPetitionerException>(() =>
            new Petitioner("Tester", phone, email, null));
    }

    // 空白的選填欄位一律存成 null，「沒填」只有一種表示方式
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreatePetitioner_BlankOptionalFields_StoredAsNull(string? blank)
    {
        var petitioner = new Petitioner("Tester", blank, "test@test.com", blank);

        Assert.Null(petitioner.Phone);
        Assert.Null(petitioner.Address);
    }

    [Fact]
    public void CreatePetitioner_SurroundingWhitespace_IsTrimmed()
    {
        var petitioner = new Petitioner("  王小明 ", " 0987654321 ", " test@test.com ", " 臺北市中山區 ");

        Assert.Equal("王小明", petitioner.Name);
        Assert.Equal("0987654321", petitioner.Phone);
        Assert.Equal("test@test.com", petitioner.Email);
        Assert.Equal("臺北市中山區", petitioner.Address);
    }

    // record 的值相等：內容相同就相等，跟是不是同一個實例無關
    [Fact]
    public void Equals_SameValuesAfterNormalization_ReturnsTrue()
    {
        var first = new Petitioner("王小明", "0987654321", null, null);
        var second = new Petitioner(" 王小明 ", "0987654321", "", "  ");

        Assert.Equal(first, second);
    }
}
