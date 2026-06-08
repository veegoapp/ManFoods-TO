using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

[Table("store_reference")]
public class StoreReference
{
    [Column("id")]
    public int Id { get; set; }

    [Column("month")]
    public int Month { get; set; }

    [Column("year")]
    public int Year { get; set; }

    [Column("store_name")]
    public string StoreName { get; set; } = "";

    [Column("store_leader")]
    public string StoreLeader { get; set; } = "";

    [Column("operation_consultant")]
    public string OperationConsultant { get; set; } = "";

    [Column("operation_manager")]
    public string OperationManager { get; set; } = "";
}
