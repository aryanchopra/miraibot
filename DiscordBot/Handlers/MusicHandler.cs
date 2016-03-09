﻿using Discord;
using Discord.Audio;
using DiscordBot.Commands;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordBot.Handlers
{
    class MusicHandler : IHandler
    {
        public static ByteBuffer Buffers;
        
        private IAudioClient AudioClient;
        private ConcurrentQueue<SongData> SongQueue = new ConcurrentQueue<SongData>();
        private const int MaxQueue = 30;

        private MusicProcessor CurrentSong;
        private Task Sending = null;

        public float Volume = 1.0f;

        public bool Playing
        {
            get
            {
                return CurrentSong != null && AudioClient != null && AudioClient.State == ConnectionState.Connected;
            }
        }

        public override string Name
        {
            get
            {
                return "Music";
            }
        }
        
        public async void Run()
        {
            SongData PlaySong;

            byte[] CurrentSend = null;
            byte[] NextSend = null;

            while (true)
            {
                try
                {
                    if (CurrentSong != null && CurrentSong.Skip)
                    {
                        /*foreach (Channel Channel in ServerData.Servers[AudioClient.Server.Id].ChannelsWithCategory("Music"))
                        {
                            Send(Channel, "Finished playing `" + CurrentSong.Song.Name + "`");
                        }*/

                        CurrentSong.Dispose();
                        CurrentSong = null;
                    }

                    if (AudioClient != null && AudioClient.State == ConnectionState.Connected)
                    {
                        if (CurrentSong == null)
                        {
                            //Dequeue a song
                            if (SongQueue.TryDequeue(out PlaySong))
                            {
                                CurrentSong = new MusicProcessor(PlaySong);

                                foreach (Channel Channel in ServerData.Servers[AudioClient.Server.Id].ChannelsWithCategory(typeof(Music).Name))
                                {
                                    SendCurrentSong(Channel);
                                    //SendPlaylist(Channel);
                                }
                            }
                            else
                            {
                                //No new song in queue
                                await Task.Delay(100);
                            }
                        }
                        else
                        {
                            await FinishSend();

                            if (NextSend != null)
                            {
                                Sending = AudioClient.OutputStream.WriteAsync(NextSend, 0, NextSend.Length);
                                if (CurrentSend != null)
                                {
                                    Buffers.Return(CurrentSend);
                                }

                                CurrentSend = NextSend;
                                NextSend = null;
                            }

                            if (CurrentSong.QueuedBuffers.Count > 0)
                            {
                                NextSend = CurrentSong.QueuedBuffers.Dequeue();
                                CurrentSong.Waiter.Release(1);

                                if (NextSend != null)
                                {
                                    NextSend = NextSend.AdjustVolume(Volume);
                                }
                            }
                            else if (CurrentSong.FinishedBuffer)
                            {
                                CurrentSong.Skip = true;
                            }
                        }
                    }
                    else
                    {
                        //Paused or not in voice chat
                        await Task.Delay(100);
                    }
                }
                catch (Exception Ex)
                {
                    $"Music Handler Loop Exception: {Ex}".Log();
                }
            }
        }

        public async Task ConnectClient(Channel VoiceChannel)
        {
            if (AudioClient == null || AudioClient.Channel.Id != VoiceChannel.Id)
            {
                if (AudioClient != null)
                {
                    await DisconnectClient();
                }

                AudioClient = await VoiceChannel.JoinAudio();
            }
        }

        public async Task DisconnectClient()
        {
            await FinishSend();

            if (AudioClient != null)
            {
                try
                {
                    await AudioClient.Disconnect();
                }
                catch { }
                AudioClient = null;
            }
        }

        private async Task FinishSend()
        {
            if (Sending != null)
            {
                try
                {
                    await Sending;
                }
                catch { }
                Sending = null;
            }
        }

        public void Enqueue(string Query, Channel Channel, bool Local = false)
        {
            if (SongQueue.Count < MaxQueue)
            {
                SongData Song = new SongData(Query, Local);
                if (Song.Found)
                {
                    SongQueue.Enqueue(Song);
                    Send(Channel, "Added `" + Song.Name + "`");
                }
                else
                {
                    Send(Channel, "Couldn't find `" + Song.Name + "`");
                }
            }
            else
            {
                Send(Channel, "The queue has reached its limit");
            }
        }

        public void Shuffle()
        {
            Random Rand = new Random();
            ConcurrentQueue<SongData> Queue = new ConcurrentQueue<SongData>();
            foreach (SongData Song in new List<SongData>(SongQueue.ToArray()).OrderBy(x => Rand.Next()))
            {
                Queue.Enqueue(Song);
            }
            
            SongQueue = Queue;
        }

        public void Push(int Place, Channel Channel)
        {
            ConcurrentQueue<SongData> NewQueue = new ConcurrentQueue<SongData>();
            List<SongData> Songs = new List<SongData>(SongQueue.ToArray());
            if (Place > 0 && Songs.Count >= Place)
            {
                SongData Pushed = Songs[Place - 1];
                NewQueue.Enqueue(Songs[Place - 1]);

                int i = 1;
                foreach (SongData Video in Songs)
                {
                    if (i++ != Place)
                    {
                        NewQueue.Enqueue(Video);
                    }
                }

                SongQueue = NewQueue;
                Send(Channel, "Pushed " + Pushed.Name + " to the top");
            }
        }

        public void Remove(List<int> Places, Channel Channel)
        {
            int Removed = 0;
            int i = 1;

            ConcurrentQueue<SongData> NewQueue = new ConcurrentQueue<SongData>();
            List<SongData> Songs = new List<SongData>(SongQueue.ToArray());
            foreach (SongData Video in Songs)
            {
                if (Places.Contains(i++))
                {
                    Removed++;
                }
                else
                {
                    NewQueue.Enqueue(Video);
                }
            }

            if (Removed > 0)
            {
                SongQueue = NewQueue;
                Send(Channel, "Removed " + Removed + " songs");
            }
        }

        public void Skip()
        {
            if (CurrentSong != null)
            {
                CurrentSong.Skip = true;
            }
        }

        public void SendCurrentSong(Channel Channel)
        {
            if (CurrentSong != null)
            {
                Send(Channel, "Now Playing `" + CurrentSong.Song.Name + "`");
            }
        }

        public void SendPlaylist(Channel Channel)
        {
            /*if (CurrentSong != null)
            {
                Text += "Now Playing\n*" + CurrentSong.Song.Name + "*\n\n";
            }*/

            SongData[] Queued = SongQueue.ToArray();
            string Text = Queued.Length + " Song(s) Queued\n";

            int Count = 0;
            foreach (SongData Entry in Queued)
            {
                Text += (++Count).ToString() + ". *" + Entry.Name + "*\n";
            }

            Send(Channel, Text);
        }

        public void Clear()
        {
            SongQueue = new ConcurrentQueue<SongData>();
        }

        public int Save(string DataFile)
        {
            SongData[] Data;
            if (CurrentSong != null)
            {
                Data = new SongData[SongQueue.Count + 1];
                Data[0] = CurrentSong.Song;
                SongQueue.CopyTo(Data, 1);
            }
            else
            {
                Data = SongQueue.ToArray();
            }

            using (BinaryWriter Writer = new BinaryWriter(File.Open(DataFile, FileMode.Create)))
            {
                Writer.Write(Data.Length);
                for (int i = 0; i < Data.Length; i++)
                {
                    Writer.Write(Data[i].Query);
                    Writer.Write(Data[i].Local);
                }
            }

            return Data.Length;
        }

        public int Load(string DataFile)
        {
            int Count = 0;
            ConcurrentQueue<SongData> Queue = new ConcurrentQueue<SongData>();

            using (BinaryReader Reader = new BinaryReader(File.Open(DataFile, FileMode.Open)))
            {
                Count = Reader.ReadInt32();
                for (int i = 0; i < Count; i++)
                {
                    Queue.Enqueue(new SongData(Reader.ReadString(), Reader.ReadBoolean()));
                }
            }

            SongQueue = Queue;
            return Count;
        }

        ~MusicHandler()
        {
            DisconnectClient().Wait();
        }
    }
}
