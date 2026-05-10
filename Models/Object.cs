using System;
using System.Runtime.Serialization;

[DataContract]
public class Object
{
    [DataMember] public int Id { get; set; }
    [DataMember] public string ObjectName { get; set; }
    [DataMember] public string ImagePath { get; set; }// Path to the object image
    [DataMember] public string SoundPath { get; set; }// Path to the sound file
    [DataMember] public int LevelId { get; set; } // Which level this object is in (1,2,3,....)
    [DataMember] public int SymbolId { get; set; } // The TUIO marker ID for this object
    [DataMember] public DateTime CreatedAt { get; set; }
    [DataMember] public bool IsDeleted { get; set; }
    [DataMember] public byte[] RowVersion { get; set; }
}