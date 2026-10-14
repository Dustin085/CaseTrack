using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseTrack.Infrastructure.Persistence;

// 在 EF 的模型上標記「這個欄位是個資」，標記跟欄位的對應寫在同一個 Configuration 裡，新增欄位時不容易漏掉。
// 只是模型的附加資訊（annotation），不影響資料表結構，不需要 migration。
public static class PersonalDataAnnotations
{
    private const string AnnotationName = "CaseTrack:PersonalData";

    public static PropertyBuilder<T> IsPersonalData<T>(this PropertyBuilder<T> builder) =>
        builder.HasAnnotation(AnnotationName, true);

    public static ComplexTypePropertyBuilder<T> IsPersonalData<T>(this ComplexTypePropertyBuilder<T> builder) =>
        builder.HasAnnotation(AnnotationName, true);

    public static bool IsPersonalData(this IReadOnlyProperty property) =>
        property.FindAnnotation(AnnotationName)?.Value is true;
}
