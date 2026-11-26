using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// The self-play policy from <c>Training/</c>, exported by <c>Training/export_unity.py</c> as
/// float16 weights in <c>Resources/TrainedBot.bytes</c>. Inputs are already in the network's
/// canonical frame: team 1 sees the board rotated 180°, exactly as in training. The export names
/// how many board layers and numbers it reads, which are always a prefix of the full encoding.
/// </summary>
public sealed class TrainedBotNetwork : IDisposable
{
    public const int Cells = GridSystem.RowCount * GridSystem.ColumnCount;
    public const int MoveActions = 5;
    public const int Actions = MoveActions + Cells;
    public const int ActiveHeadPlane = 8;
    public const int LegacyPlanes = 24;
    public const string ResourceName = "TrainedBot";

    private const int Width = 64;
    private const int Hidden = 256;
    private const int HeaderBytes = 16;
    private static readonly byte[] Magic = { (byte)'B', (byte)'P', (byte)'N', (byte)'2' };

    private static TrainedBotNetwork shared;

    private NativeArray<float> weights;
    private NativeArray<float> planes;
    private NativeArray<float> scalars;
    private NativeArray<byte> mask;
    private NativeArray<float> boardA;
    private NativeArray<float> boardB;
    private NativeArray<float> patches;
    private NativeArray<float> features;
    private NativeArray<float> context;
    private NativeArray<float> logits;

    public static TrainedBotNetwork Shared => shared ??= Load();

    public int Planes { get; }
    public int Scalars { get; }
    public bool Revises => Planes > LegacyPlanes;

    private TrainedBotNetwork(NativeArray<float> weights, int planeCount, int scalarCount)
    {
        this.weights = weights;
        Planes = planeCount;
        Scalars = scalarCount;
        planes = new NativeArray<float>(planeCount * Cells, Allocator.Persistent);
        scalars = new NativeArray<float>(scalarCount, Allocator.Persistent);
        mask = new NativeArray<byte>(Actions, Allocator.Persistent);
        boardA = new NativeArray<float>(Width * Cells, Allocator.Persistent);
        boardB = new NativeArray<float>(Width * Cells, Allocator.Persistent);
        patches = new NativeArray<float>(Cells * Mathf.Max(Width, planeCount) * 9, Allocator.Persistent);
        features = new NativeArray<float>(Hidden, Allocator.Persistent);
        context = new NativeArray<float>(Width, Allocator.Persistent);
        logits = new NativeArray<float>(Actions, Allocator.Persistent);
        Application.quitting += Dispose;
#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Dispose;
#endif
    }

    private static TrainedBotNetwork Load()
    {
        TextAsset asset = Resources.Load<TextAsset>(ResourceName);
        if (asset == null)
            throw new InvalidOperationException($"Resources/{ResourceName}.bytes is missing.");

        byte[] bytes = asset.bytes;
        Resources.UnloadAsset(asset);
        for (int i = 0; i < Magic.Length; i++)
        {
            if (bytes[i] != Magic[i])
                throw new InvalidOperationException($"{ResourceName}.bytes is not a policy export.");
        }

        int planeCount = BitConverter.ToInt32(bytes, 4);
        int scalarCount = BitConverter.ToInt32(bytes, 8);
        int count = BitConverter.ToInt32(bytes, 12);
        int expected = Forward.WeightCount(planeCount, scalarCount);
        if (count != expected || bytes.Length != HeaderBytes + count * 2)
        {
            throw new InvalidOperationException(
                $"{ResourceName}.bytes holds {count} weights; {planeCount} layers and {scalarCount} numbers need {expected}."
            );
        }

        NativeArray<float> weights = new(count, Allocator.Persistent);
        for (int i = 0; i < count; i++)
            weights[i] = Mathf.HalfToFloat(BitConverter.ToUInt16(bytes, HeaderBytes + i * 2));
        return new TrainedBotNetwork(weights, planeCount, scalarCount);
    }

    /// <summary>
    /// Reads the first <see cref="Planes"/> layers and <see cref="Scalars"/> numbers, fills
    /// <paramref name="output"/> with masked logits, and returns the best action.
    /// </summary>
    public int Evaluate(float[] inputPlanes, float[] inputScalars, bool[] legal, float[] output = null)
    {
        NativeArray<float>.Copy(inputPlanes, planes, Planes * Cells);
        NativeArray<float>.Copy(inputScalars, scalars, Scalars);
        int legalTargets = 0;
        for (int i = 0; i < Actions; i++)
        {
            mask[i] = legal[i] ? (byte)1 : (byte)0;
            if (legal[i] && i >= MoveActions)
                legalTargets++;
        }

        new Forward
        {
            W = weights,
            Planes = planes,
            Scalars = scalars,
            Mask = mask,
            A = boardA,
            B = boardB,
            Patches = patches,
            Features = features,
            Context = context,
            Logits = logits,
            InputPlanes = Planes,
            InputScalars = Scalars,
            TargetOffset = Mathf.Log(Mathf.Max(1, legalTargets)),
        }.Run();

        int best = 0;
        for (int i = 1; i < Actions; i++)
        {
            if (logits[i] > logits[best])
                best = i;
        }
        if (output != null)
            logits.CopyTo(output);
        return best;
    }

    public void Dispose()
    {
        Application.quitting -= Dispose;
#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
#endif
        if (!weights.IsCreated)
            return;
        weights.Dispose();
        planes.Dispose();
        scalars.Dispose();
        mask.Dispose();
        boardA.Dispose();
        boardB.Dispose();
        patches.Dispose();
        features.Dispose();
        context.Dispose();
        logits.Dispose();
        if (shared == this)
            shared = null;
    }

    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    private struct Forward : IJob
    {
        private const int Rows = GridSystem.RowCount;
        private const int Columns = GridSystem.ColumnCount;

        [ReadOnly] public NativeArray<float> W;
        [ReadOnly] public NativeArray<float> Planes;
        [ReadOnly] public NativeArray<float> Scalars;
        [ReadOnly] public NativeArray<byte> Mask;
        public NativeArray<float> A;
        public NativeArray<float> B;
        public NativeArray<float> Patches;
        public NativeArray<float> Features;
        public NativeArray<float> Context;
        public NativeArray<float> Logits;
        public int InputPlanes;
        public int InputScalars;
        public float TargetOffset;

        public static int WeightCount(int planeCount, int scalarCount)
        {
            int convolutions = Width * planeCount * 9 + Width + 2 * (Width * Width * 9 + Width);
            int body = Hidden * (Width * Cells + scalarCount) + Hidden;
            int heads = Width * Hidden + Width + 2 * (Width + 1) + MoveActions * Hidden + MoveActions;
            return convolutions + body + heads;
        }

        public void Execute()
        {
            int conv0W = 0;
            int conv0B = conv0W + Width * InputPlanes * 9;
            int conv1W = conv0B + Width;
            int conv1B = conv1W + Width * Width * 9;
            int conv2W = conv1B + Width;
            int conv2B = conv2W + Width * Width * 9;
            int bodyInputs = Width * Cells + InputScalars;
            int bodyW = conv2B + Width;
            int bodyB = bodyW + Hidden * bodyInputs;
            int contextW = bodyB + Hidden;
            int contextB = contextW + Width * Hidden;
            int targetW = contextB + Width;
            int targetB = targetW + Width;
            int stepW = targetB + 1;
            int stepB = stepW + Width;
            int moveW = stepB + 1;
            int moveB = moveW + MoveActions * Hidden;

            Convolve(Planes, InputPlanes, A, conv0W, conv0B);
            Convolve(A, Width, B, conv1W, conv1B);
            Convolve(B, Width, A, conv2W, conv2B);

            for (int j = 0; j < Hidden; j++)
            {
                int row = bodyW + j * bodyInputs;
                float sum = W[bodyB + j];
                for (int k = 0; k < Width * Cells; k++)
                    sum += W[row + k] * A[k];
                for (int k = 0; k < InputScalars; k++)
                    sum += W[row + Width * Cells + k] * Scalars[k];
                Features[j] = Relu(sum);
            }

            for (int c = 0; c < Width; c++)
            {
                float sum = W[contextB + c];
                for (int j = 0; j < Hidden; j++)
                    sum += W[contextW + c * Hidden + j] * Features[j];
                Context[c] = sum;
            }

            for (int c = 0; c < Width; c++)
            {
                for (int p = 0; p < Cells; p++)
                    B[c * Cells + p] = Relu(A[c * Cells + p] + Context[c]);
            }

            int head = 0;
            for (int p = 1; p < Cells; p++)
            {
                if (Planes[ActiveHeadPlane * Cells + p] > Planes[ActiveHeadPlane * Cells + head])
                    head = p;
            }
            int headX = head % Columns;
            int headY = head / Columns;
            for (int m = 0; m < MoveActions; m++)
            {
                int x = headX + (m == 2 ? 1 : m == 4 ? -1 : 0);
                int y = headY + (m == 1 ? 1 : m == 3 ? -1 : 0);
                int cell = x >= 0 && x < Columns && y >= 0 && y < Rows ? y * Columns + x : head;
                float sum = W[moveB + m] + CellScore(stepW, stepB, cell);
                for (int j = 0; j < Hidden; j++)
                    sum += W[moveW + m * Hidden + j] * Features[j];
                Logits[m] = sum;
            }

            for (int p = 0; p < Cells; p++)
            {
                Logits[MoveActions + p] =
                    Mask[MoveActions + p] != 0 ? CellScore(targetW, targetB, p) - TargetOffset : 0f;
            }

            for (int a = 0; a < Actions; a++)
            {
                if (Mask[a] == 0)
                    Logits[a] = -1e9f;
            }
        }

        private float CellScore(int weightOffset, int biasOffset, int cell)
        {
            float sum = W[biasOffset];
            for (int c = 0; c < Width; c++)
                sum += W[weightOffset + c] * B[c * Cells + cell];
            return sum;
        }

        private void Convolve(
            NativeArray<float> input,
            int inputChannels,
            NativeArray<float> output,
            int weightOffset,
            int biasOffset
        )
        {
            int patchSize = inputChannels * 9;
            for (int p = 0; p < Cells; p++)
            {
                int x = p % Columns;
                int y = p / Columns;
                int patch = p * patchSize;
                for (int i = 0; i < inputChannels; i++)
                {
                    for (int ky = 0; ky < 3; ky++)
                    {
                        int sourceY = y + ky - 1;
                        for (int kx = 0; kx < 3; kx++)
                        {
                            int sourceX = x + kx - 1;
                            bool inside = sourceX >= 0 && sourceX < Columns && sourceY >= 0 && sourceY < Rows;
                            Patches[patch + i * 9 + ky * 3 + kx] = inside
                                ? input[i * Cells + sourceY * Columns + sourceX]
                                : 0f;
                        }
                    }
                }
            }

            for (int o = 0; o < Width; o++)
            {
                int row = weightOffset + o * patchSize;
                float bias = W[biasOffset + o];
                for (int p = 0; p < Cells; p++)
                {
                    int patch = p * patchSize;
                    float sum = bias;
                    for (int k = 0; k < patchSize; k++)
                        sum += W[row + k] * Patches[patch + k];
                    output[o * Cells + p] = Relu(sum);
                }
            }
        }

        private static float Relu(float value) => value > 0f ? value : 0f;
    }
}
