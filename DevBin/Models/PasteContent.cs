using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text;

namespace DevBin.Models;

public class PasteContent
{
    [Key]
    [Column(TypeName = "BINARY(32)")]
    public required byte[] HashId { get; set; }
    
    public required byte[] Content { get; set; }
    
    [NotMapped]
    internal virtual string StringContent => Encoding.UTF8.GetString(Content);

    public static byte[] Hash(byte[] content)
    {
        return SHA256.HashData(content);
    }
}