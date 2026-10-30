using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Departments;
using CaseTrack.Application.Exceptions;
using CaseTrack.Domain.Departments;

namespace CaseTrack.Tests;

// 兩個管理者同時新增同一個代碼：兩邊的 CodeExistsAsync 都回答「不存在」，後存的被唯一索引擋下。
// 整合測試很難剛好卡在「查完、還沒存」之間，所以用假的依賴製造這個時間點
public class ManageDepartmentsHandlerTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task CreateAsync_WhenUniqueIndexRejectsConcurrentInsert_ThrowsValidationForCode()
    {
        var handler = new ManageDepartmentsHandler(new EmptyDepartmentRepository(), new DuplicateKeyUnitOfWork());

        var exception = await Assert.ThrowsAsync<RequestValidationException>(
            () => handler.CreateAsync("cul", "文化局", Ct));

        Assert.Equal("Code", exception.Field);
        Assert.Equal("代碼 CUL 已經存在", exception.Message);
    }

    // ── 假的依賴 ─────────────────────────────────────────

    // 查詢時代碼還不存在（另一個請求還沒存檔）
    private sealed class EmptyDepartmentRepository : IDepartmentRepository
    {
        public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) => Task.FromResult(false);

        public void Add(Department department)
        {
        }

        public Task<bool> IsActiveAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Department?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DepartmentSummary>> ListAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    // 存檔時另一個請求已經先存了同一個代碼：Infrastructure 會把 SqlException 2601 轉成這個例外
    private sealed class DuplicateKeyUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new UniqueConstraintViolationException("資料違反唯一性限制", new Exception("2601"));
    }
}
