using System.IO;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
[Category("TrainedBot")]
public class TrainedBotNetworkEditModeTests
{
    private static string FixturePath =>
        Path.Combine(Application.dataPath, "..", "Training", "fixtures", "trained_bot_logits.bin");

    [Test]
    public void MatchesPyTorchOnRecordedDecisions()
    {
        using BinaryReader reader = new(File.OpenRead(FixturePath));
        int samples = reader.ReadInt32();
        TrainedBotNetwork network = TrainedBotNetwork.Shared;
        float[] planes = new float[network.Planes * TrainedBotNetwork.Cells];
        float[] scalars = new float[network.Scalars];
        bool[] mask = new bool[TrainedBotNetwork.Actions];
        float[] expected = new float[TrainedBotNetwork.Actions];
        float[] actual = new float[TrainedBotNetwork.Actions];

        for (int sample = 0; sample < samples; sample++)
        {
            Read(reader, planes);
            Read(reader, scalars);
            for (int i = 0; i < mask.Length; i++)
                mask[i] = reader.ReadByte() != 0;
            Read(reader, expected);

            int chosen = network.Evaluate(planes, scalars, mask, actual);

            int best = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] > expected[best])
                    best = i;
                if (mask[i])
                {
                    float tolerance = 1e-3f * Mathf.Max(1f, Mathf.Abs(expected[i]));
                    Assert.That(actual[i], Is.EqualTo(expected[i]).Within(tolerance), $"sample {sample}, action {i}");
                }
            }
            Assert.That(chosen, Is.EqualTo(best), $"sample {sample}");
        }
    }

    private static void Read(BinaryReader reader, float[] values)
    {
        for (int i = 0; i < values.Length; i++)
            values[i] = reader.ReadSingle();
    }
}
