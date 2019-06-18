using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DAGLabs_Ex
{
    public class Block
    {
        public string ID { get; set; }
        public Block[] Parents { get; set; }

        public override string ToString()
        {
            return ID.ToString();
        }
    }

    public class DAG
    {
        public Block[] Tips { get; set; }

        /// <summary>
        /// Used for synchronizing access when manipulating DAG
        /// </summary>
        public object SyncObject { get; set; } = new object();
    }

    public class Miner
    {
        public int Index { get; set; }
        public DAG DAG { get; set; }
        public Miner[] ConnectedMiners { get; set; }

        public override string ToString()
        {
            return Index.ToString();
        }
    }

    class Network
    {
        readonly Random rand = new Random(42);  // Setting seed for testability

        readonly int BlockCount = 10;
        readonly int MinerCount = 10;
        readonly TimeSpan CreationRate = TimeSpan.FromSeconds(3);
        readonly TimeSpan PropagationDelay = TimeSpan.FromSeconds(3);

        readonly Block GenesisBlock = new Block { ID = "00" };

        DateTime lastBlockCreationTime = DateTime.Now;

        // Used to populate new block ID. 
        // Currently limited to 26 English characters, but is easy to extend.
        readonly Queue<string> blockIDs = new Queue<string>();

        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        public void Start()
        {
            // init block IDs for verbosity (instead of using int or guid)
            foreach (char c in chars)
                blockIDs.Enqueue(c.ToString());

            // spawn miners
            var miners = new Miner[MinerCount];

            for (int i = 0; i < miners.Length; i++)
            {
                // create the same DAG for every miner - only genesis block
                var dag = new DAG
                {
                    Tips = new Block[] { GenesisBlock }
                };

                miners[i] = new Miner { Index = i, DAG = dag };

                Console.WriteLine("Spawned miner #{0}", i);
            }

            // Set ConnectedMiners for every miner.
            // Currently every miner knows all the others, but it is easy to plug-in any other
            // network layout here (i.e. every miner is connected to 50% of the miners, etc.)
            for (int i = 0; i < miners.Length; i++)
            {
                var miner = miners[i];

                miner.ConnectedMiners = miners
                    .Except(new[] { miner })
                    .ToArray();
            }

            // only genesis block exists at this point
            int currentBlockCount = 1;

            while (currentBlockCount < BlockCount)
            {
                if (lastBlockCreationTime.Add(CreationRate) > DateTime.Now)
                {
                    Thread.Sleep(500);
                    continue;
                }

                int minerIndex = rand.Next(MinerCount);
                
                var miner = miners[minerIndex];

                CreateNewBlock(miner);

                currentBlockCount++;

                lastBlockCreationTime = DateTime.Now;
            }
        }

        void CreateNewBlock(Miner miner)
        {
            var dag = miner.DAG;
            var tips = dag.Tips;

            var parents = new Block[0];
            
            // keep generating random subset until non-empty is returned
            while(parents.Length == 0)
            {
                parents = GetRandomSubset(tips);
            }

            var newBlock = new Block
            {
                ID = blockIDs.Dequeue(),
                Parents = parents
            };

            Console.WriteLine("Miner #{0} created new block with ID = '{1}' and parents: {2}",
                miner.Index, newBlock.ID, string.Join<Block>(", ", newBlock.Parents));

            // Add the new block to the current miner's DAG
            AddBlock(dag, newBlock);

            foreach (var knownMiner in miner.ConnectedMiners)
            {
                SendNewBlockToMiner(knownMiner, newBlock);
            }
        }

        void SendNewBlockToMiner(Miner miner, Block newBlock)
        {
            // Randomize offset in the interval [-0.1, 0.1)
            double offset = (rand.NextDouble() - 0.5) / 5;

            double delay = PropagationDelay.TotalSeconds * (1 + offset);

            var delaySpan = TimeSpan.FromSeconds(delay);

            Task.Factory.StartNew(() =>
            {
                Thread.Sleep(delaySpan);

                AddBlock(miner.DAG, newBlock);

                Console.WriteLine("Added block with ID = {0} to miner #{1} after {2} seconds",
                    newBlock.ID, miner.Index, delay);
            });
        }

        /// <summary>
        /// Returns a random subset of blocks
        /// </summary>
        /// <param name="blocks"></param>
        /// <returns></returns>
        Block[] GetRandomSubset(Block[] blocks)
        {
            if (blocks.Length == 1)
                return blocks;

            byte[] buffer = new byte[blocks.Length];

            rand.NextBytes(buffer);

            var parents = new List<Block>();

            for (int i = 0; i < buffer.Length; i++)
            {
                byte b = buffer[i];

                // this makes about half the blocks become parents of the new block
                if (b > 127)
                {
                    parents.Add(blocks[i]);
                }
            }

            return parents.ToArray();
        }

        void AddBlock(DAG dag, Block newBlock)
        {
            lock (dag.SyncObject)
            {
                var oldTips = dag.Tips;

                // create the new Tips list and add 'newBlock'
                var newTips = new List<Block> { newBlock };

                // Add every tip that is not a parent of 'newBlock'
                foreach (var block in oldTips)
                {
                    bool isParentOfNewBlock = false;

                    // check if 'block' is a parent of 'newBlock'
                    foreach (var newBlockParent in newBlock.Parents)
                    {
                        if (block.ID == newBlockParent.ID)
                        {
                            isParentOfNewBlock = true;
                            break;
                        }
                    }

                    if (isParentOfNewBlock == false)
                    {
                        // only add blocks that are not parent of 'newBlock'
                        newTips.Add(block);
                    }
                }

                dag.Tips = newTips.ToArray();
            }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            new Network().Start();
        }
    }
}
