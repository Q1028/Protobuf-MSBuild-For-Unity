using UnityEngine;
using Google.Protobuf;
using TTT.Protobuf.Messages;

public class ExampleUsage : MonoBehaviour
{
    void Start()
    {
        var p = new Player { Id = 1, Name = "Alice" };
        byte[] bytes = p.ToByteArray();
        var p2 = Player.Parser.ParseFrom(bytes);
        Debug.Log($"Player: id={p2.Id}, name={p2.Name}");
    }
}