using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

[Table("upload_logs")]
public class UploadLog
{
    [Column("id")]
    public int Id { get; set; }

    [Column("file_type")]
    public string FileType { get; set; } = "";

    [Column("file_name")]
    public string FileName { get; set; } = "";

    [Column("month")]
    public int Month { get; set; }

    [Column("year")]
    public int Year { get; set; }

    [Column("upload_date")]
    public DateTime UploadDate { get; set; } = DateTime.UtcNow;

    [Column("uploaded_by")]
    public string UploadedBy { get; set; } = "";

    [Column("file_content")]
    public byte[]? FileContent { get; set; }

    [Column("content_type")]
    public string? ContentType { get; set; }

    [NotMapped]
    public bool HasFile { get; set; }
}
