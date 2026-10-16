using Bacon.Caching.Redis.Services;

namespace Bacon.Caching.Redis.Tests.Services;

/// <summary>
/// Tests the pure batching helpers of the cluster service. The commands themselves need a real cluster.
/// </summary>
[TestFixture]
internal sealed class ClusterCacheServiceTests
{
    private static string[] Keys(int count) => [.. Enumerable.Range(0, count).Select(index => $"key{index}")];

    private static (string Key, int Index)[] Batch(int size, int start) => [.. Enumerable.Range(start, size).Select(index => ($"key{index}", index))];

    private static readonly int[] _chunkSizes = [4, 4, 2];

    private static readonly int[] _slotGroupSizes = [3, 3, 4];

    private static readonly int[] _twoWavesOfTwoBatches = [2, 2];

    private static readonly int[] _threeWavesOfOneBatch = [1, 1, 1];

    #region CreateBatches

    [Test]
    public void CreateBatches_NoKeys_ReturnsNoBatches()
    {
        (string Key, int Index)[][] batches = ClusterCacheService<object>.CreateBatches([], 4, _ => -1);

        Assert.That(batches, Is.Empty);
    }

    [Test]
    public void CreateBatches_SingleSlot_ChunksByBatchSizeKeepingOrderAndIndexes()
    {
        string[] keys = Keys(10);

        (string Key, int Index)[][] batches = ClusterCacheService<object>.CreateBatches(keys, 4, _ => -1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(batches.Select(batch => batch.Length), Is.EqualTo(_chunkSizes));
            Assert.That(batches.SelectMany(batch => batch).Select(item => item.Index), Is.EqualTo(Enumerable.Range(0, 10)));
            Assert.That(batches.SelectMany(batch => batch).All(item => item.Key == keys[item.Index]), Is.True);
        };
    }

    [Test]
    public void CreateBatches_MixedSlots_NeverMixesSlotsAndCoversEveryKeyOnce()
    {
        string[] keys = Keys(10);

        (string Key, int Index)[][] batches = ClusterCacheService<object>.CreateBatches(keys, 2, key => key[^1] % 3);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(batches.All(batch => batch.Select(item => item.Key[^1] % 3).Distinct().Count() == 1), Is.True);
            Assert.That(batches.All(batch => batch.Length <= 2), Is.True);
            Assert.That(batches.SelectMany(batch => batch).Select(item => item.Index).Order(), Is.EqualTo(Enumerable.Range(0, 10)));
            Assert.That(batches.SelectMany(batch => batch).All(item => item.Key == keys[item.Index]), Is.True);
        };
    }

    [Test]
    public void CreateBatches_MixedSlotsWithLargeBatchSize_DoesNotMergeSlots()
    {
        // key0, key3, key6, key9 -> slot 0; key1, key4, key7 -> slot 1; key2, key5, key8 -> slot 2
        (string Key, int Index)[][] batches = ClusterCacheService<object>.CreateBatches(Keys(10), 100, key => key[^1] % 3);

        Assert.That(batches.Select(batch => batch.Length).Order(), Is.EqualTo(_slotGroupSizes));
    }

    [Test]
    public void CreateBatches_BatchSizeOne_ReturnsOneKeyPerBatch()
    {
        (string Key, int Index)[][] batches = ClusterCacheService<object>.CreateBatches(Keys(5), 1, key => key[^1] % 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(batches, Has.Length.EqualTo(5));
            Assert.That(batches.All(batch => batch.Length == 1), Is.True);
        };
    }

    #endregion

    #region CreateWaves

    [Test]
    public void CreateWaves_NoBatches_ReturnsNoWaves()
    {
        List<(string Key, int Index)[][]> waves = ClusterCacheService<object>.CreateWaves([], 10);

        Assert.That(waves, Is.Empty);
    }

    [Test]
    public void CreateWaves_BatchesWithinTheLimit_ReturnsASingleWave()
    {
        (string Key, int Index)[][] batches = [Batch(2, 0), Batch(2, 2), Batch(2, 4)];

        List<(string Key, int Index)[][]> waves = ClusterCacheService<object>.CreateWaves(batches, 6);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(waves, Has.Count.EqualTo(1));
            Assert.That(waves[0], Has.Length.EqualTo(3));
        };
    }

    [Test]
    public void CreateWaves_BatchesExceedingTheLimit_StartANewWave()
    {
        (string Key, int Index)[][] batches = [Batch(2, 0), Batch(2, 2), Batch(2, 4), Batch(2, 6)];

        List<(string Key, int Index)[][]> waves = ClusterCacheService<object>.CreateWaves(batches, 5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(waves, Has.Count.EqualTo(2));
            Assert.That(waves.Select(wave => wave.Length), Is.EqualTo(_twoWavesOfTwoBatches));
        };
    }

    [Test]
    public void CreateWaves_BatchLargerThanTheLimit_StillGetsItsOwnWave()
    {
        (string Key, int Index)[][] batches = [Batch(10, 0)];

        List<(string Key, int Index)[][]> waves = ClusterCacheService<object>.CreateWaves(batches, 4);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(waves, Has.Count.EqualTo(1));
            Assert.That(waves[0], Has.Length.EqualTo(1));
        };
    }

    [Test]
    public void CreateWaves_SmallLargeSmall_ReturnsThreeWaves()
    {
        (string Key, int Index)[][] batches = [Batch(1, 0), Batch(10, 1), Batch(1, 11)];

        List<(string Key, int Index)[][]> waves = ClusterCacheService<object>.CreateWaves(batches, 5);

        Assert.That(waves.Select(wave => wave.Length), Is.EqualTo(_threeWavesOfOneBatch));
    }

    [Test]
    public void CreateWaves_KeepsEveryBatchOnceInOrder()
    {
        (string Key, int Index)[][] batches = [Batch(3, 0), Batch(1, 3), Batch(4, 4), Batch(2, 8), Batch(5, 10), Batch(1, 15)];

        List<(string Key, int Index)[][]> waves = ClusterCacheService<object>.CreateWaves(batches, 6);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(waves.SelectMany(wave => wave).SelectMany(batch => batch).Select(item => item.Index), Is.EqualTo(Enumerable.Range(0, 16)));
            Assert.That(waves.All(wave => wave.Length == 1 || wave.Sum(batch => batch.Length) <= 6), Is.True);
        };
    }

    #endregion
}