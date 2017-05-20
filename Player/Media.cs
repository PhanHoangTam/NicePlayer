﻿using HtmlAgilityPack;
using Player.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;


namespace Player
{
    public class Media
    {
        [DllImport("user32")]
        public static extern void LockWorkStation();

        [DllImport("user32")]
        public static extern bool ExitWindowsEx(uint flags, uint reason);
        
        public AxWMPLib.AxWindowsMediaPlayer wmp = new AxWMPLib.AxWindowsMediaPlayer();
        
        string dirPlaylist = string.Empty;  // Lưu tên playlist đang phát
        List<string> dirMedia = new List<string>();  // Lưu danh sách đường dẫn các media đang phát
        List<string> urlYouTube = new List<string>();  // Lưu danh sách URL từ YouTube dựa vào keyword (Dùng cho Karaoke)
        List<string> dirLocation = new List<string>();  // Lưu danh sách đường dẫn các thư mục chứa media
        
        bool found = false;  // Kết quả tìm kiếm media
        string itemClicked = string.Empty;  // Lấy tên media được click trong Search Result
        
        ToolTip tip = new ToolTip();


/***************************************************************************************/


        // Hiệu ứng zoom-in cho các Button
        public void mouseHover(Image image, PictureBox box, string message)
        {
            tip = new ToolTip();
            tip.Show(message, box);

            int width = image.Width + 5;
            int height = image.Height + 5;
            Bitmap bmp = new Bitmap(width, height);
            Graphics g = Graphics.FromImage(bmp);
            g.DrawImage(image, new Rectangle(Point.Empty, bmp.Size));
            box.Image = bmp;
        }


/***************************************************************************************/


        // Hiệu ứng zoom-out cho các Button
        public void mouseLeave(Image image, PictureBox box)
        {
            tip.RemoveAll();
            box.Image = image;
        }


/***************************************************************************************/


        // Trả về link bài hát dựa vào tên bài hát
        public string getLink(HtmlAgilityPack.HtmlDocument doc, ListView listView)
        {
            string link = string.Empty;

            try
            {
                foreach (HtmlNode node in doc.DocumentNode.SelectNodes("//a[@href]"))
                {
                    if (node.Attributes["href"].Value.StartsWith("http://m1"))
                    {
                        link = node.Attributes["href"].Value;
                        break;
                    }
                }
            }
            catch
            {
                
            }

            return link;
        }


/***************************************************************************************/


        // Trả về lyric dựa vào link đã có
        public string getLyric(HtmlAgilityPack.HtmlDocument doc)
        {
            string lyric = string.Empty;
            string[] pattern = new string[] { @"<span[^>]*>[\s\S]*?</span>", @"&quot;" };
            Regex regex = new Regex(string.Join("|", pattern));

            // Load và chỉnh sửa HTML 
            try
            {
                string HTML = doc.DocumentNode.InnerHtml;
                HTML = regex.Replace(HTML, "\n");
                doc.LoadHtml(HTML);
            }
            catch
            {

            }

            // Lấy lyric của media từ HTML đã chỉnh sửa
            try
            {
                HtmlNode node = doc.DocumentNode.SelectSingleNode("//p[@class='genmed']");
                lyric = node.InnerText;
            }
            catch
            {
                lyric = "502 Bad Gateway";
            }
            
            return lyric.Replace("\n\n\n\n", "\n").Replace("\n\n\n", "\n");
        }


/***************************************************************************************/


        // Chuyển đổi tiếng Việt sang không dấu => Dùng cho tìm kiếm media
        public string convertText(string text)
        {
            Regex regex = new Regex("\\p{IsCombiningDiacriticalMarks}+");
            return regex.Replace(text.Normalize(NormalizationForm.FormD), String.Empty).Replace('\u0111', 'd').Replace('\u0110', 'D');
        }


/***************************************************************************************/


        // Lấy Title + URL từ YouTube
        public void getURL(ListView listView, HtmlAgilityPack.HtmlDocument doc)
        {
            listView.Clear();
            urlYouTube.Clear();

            try
            {
                foreach (HtmlNode node in doc.DocumentNode.SelectNodes("//a[@title]"))
                {
                    if (node.Attributes["href"].Value.StartsWith("/watch?v=") && !node.Attributes["href"].Value.StartsWith("/channel") && !node.Attributes["href"].Value.Contains("list") && node.Attributes["title"].Value.ToLower().Contains("karaoke"))
                    {
                        listView.Items.Add(node.Attributes["title"].Value.Replace("amp;", string.Empty));
                        urlYouTube.Add("https://www.youtube.com" + node.Attributes["href"].Value.Replace("watch?v=", "v/"));
                    }
                }
            }
            catch
            {

            }
        }


/***************************************************************************************/


        // Xử lý click trái cho lvPlaying, lvPlaylist, lvLocation
        public void leftClick(ListView listView, string type)
        {
            if (type == "Now Playing")  // Xử lý click trái cho lvPlaying => Update dirMedia
            {
                // Tạo playlist mới
                WMPLib.IWMPPlaylist playlist = wmp.newPlaylist(string.Empty, string.Empty);

                for(int i = 0; i < dirMedia.Count; i++)
                {
                    // Nếu media được chọn = media[i]
                    if(Path.GetFileName(dirMedia[i]) == listView.FocusedItem.Text)
                    {
                        if(found)  // Nếu media[i] nằm trong Search Result
                        {
                            wmp.URL = dirMedia[i];
                            itemClicked = Path.GetFileName(dirMedia[i]);  // Lấy tên media được chọn => Hiển thị lên Now Playing khi ấn nút Listen
                            break;
                        }
                        else  // Nếu media[i] nằm trong danh sách phát
                        {
                            playlist.appendItem(wmp.newMedia(dirMedia[i]));  // Thêm media[i] vào playlist
                            
                            // Thêm các media đứng sau media[i] vào playlist
                            for(int j = i + 1; j < dirMedia.Count; j++)
                            {
                                playlist.appendItem(wmp.newMedia(dirMedia[j]));
                            }

                            wmp.currentPlaylist = playlist;
                            break;
                        }
                    }
                }
            }
            else if (type == "Playlist")  // Xử lý click trái cho lvPlaylist
            {
                dirMedia.Clear();

                try
                {
                    string line = string.Empty;
                    FileStream stream = new FileStream(@"Playlist\" + listView.FocusedItem.Text + ".txt", FileMode.Open);
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8);

                    while ((line = reader.ReadLine()) != null)
                    {
                        dirMedia.Add(line);  // Lưu đường dẫn các media trong playlist được chọn
                    }

                    reader.Close();
                    stream.Close();
                }
                catch
                {

                }
            }
            else  // Xử lý click trái cho lvLocation
            {
                dirMedia.Clear();

                foreach (string item in Directory.GetFiles(listView.FocusedItem.Text))
                {
                    if (item.EndsWith(".mp3") || item.EndsWith(".wav") || item.EndsWith(".flac") || item.EndsWith(".mpg") || item.EndsWith(".mp4") || item.EndsWith(".mkv") || item.EndsWith(".vob"))
                        dirMedia.Add(item);  // Lưu đường dẫn các media trong thư mục được chọn
                }
            }
        }


/***************************************************************************************/


        // Xử lý click phải cho lvPlaying, lvPlaylist, lvLocation
        public void rightClick(ListView listView, RichTextBox rtbLyric, string type)
        {
            // Khai báo 1 ContextMenuStrip + 3 ToolStripItem tùy chọn cho từng loại ListView
            ContextMenuStrip context = new ContextMenuStrip();
            listView.ContextMenuStrip = context;
            
            ToolStripMenuItem iLyric = new ToolStripMenuItem("Lyric");
            ToolStripMenuItem iDelete = new ToolStripMenuItem("Delete");
            ToolStripMenuItem iAddTo = new ToolStripMenuItem("Add To");
            ToolStripMenuItem iProperty = new ToolStripMenuItem("Property");
            ToolStripMenuItem iRename = new ToolStripMenuItem("Rename");
            
            
            if (type == "Now Playing")  // Xử lý click phải cho lvPlaying
            {
                try
                {
                    // Thêm tất cả tên playlist vào iAddTo
                    foreach (string item in Directory.GetFiles(@"Playlist", "*.txt"))
                    {
                        iAddTo.DropDownItems.Add(new ToolStripMenuItem(Path.GetFileNameWithoutExtension(item)));
                    }
                }
                catch
                {

                }
                

                // Thêm 3 ToolStripItem : Lyric, Delete, Property
                context.Items.AddRange(new ToolStripItem[] { iLyric, iDelete, iAddTo, iProperty });


                // Chỉ hiển thị ContextMenuStrip khi listView.SelectedItems.Count != 0
                context.Opening += (sender, args) =>
                {
                    args.Cancel = listView.SelectedItems.Count == 0;
                };


                // Xem lyric
                iLyric.Click += (sender, args) =>
                {
                    rtbLyric.Visible = true;
                    string index = listView.FocusedItem.Text.Substring(0, listView.FocusedItem.Text.Length - 4);
                    index.Replace("-", " ").Replace(" ", "+");

                    HtmlWeb web = new HtmlWeb();
                    HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();

                    // Load HTML chứa các link phù hợp với tên media
                    try
                    {
                        doc = web.Load("http://search.chiasenhac.vn/search.php?s=" + index);
                    }
                    catch
                    {
                        MessageBox.Show("502 Bad Gateway", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                    // Load HTML của 1 link phù hợp với tên media nhất 
                    try
                    {
                        doc = web.Load(getLink(doc, listView));
                    }
                    catch
                    {

                    }
                    
                    rtbLyric.Text = listView.FocusedItem.Text.Substring(0, listView.FocusedItem.Text.Length - 4).ToUpper() + "\n\n" + getLyric(doc) + "\n";
                };


                // Xóa media khỏi 1 playlist
                iDelete.Click += (sender, args) =>
                {
                    // Xóa đường dẫn của media cần xóa khỏi dirMedia
                    foreach (string item in dirMedia)
                    {
                        if (Path.GetFileName(item) == listView.FocusedItem.Text)
                        {
                            dirMedia.Remove(item);
                            break;
                        }
                    }

                    File.Delete(@"Playlist\" + dirPlaylist + ".txt");  // Xóa file .txt của playlist chứa media cần xóa

                    try
                    {
                        // Tạo mới file .txt của playlist chứa media cần xóa
                        FileStream stream = new FileStream(@"Playlist\" + dirPlaylist + ".txt", FileMode.CreateNew);
                        StreamWriter writer = new StreamWriter(stream, Encoding.UTF8);

                        foreach (string item in dirMedia)
                        {
                            writer.WriteLine(item);  // Ghi các đường dẫn media đã update
                        }

                        writer.Close();
                        stream.Close();
                    }
                    catch
                    {

                    }

                    listView.Items.Remove(listView.FocusedItem);  // Xóa tên media khỏi lvPlaying
                };


                // Thêm media vào 1 playlist khác
                iAddTo.DropDownItemClicked += (sender, args) =>
                {
                    context.Hide();
                    bool available = false;  // Playlist chưa tồn tại media cần thêm

                    try
                    {
                        string line = string.Empty;
                        FileStream stream = new FileStream(@"Playlist\" + args.ClickedItem.Text + ".txt", FileMode.Open);
                        StreamReader reader = new StreamReader(stream, Encoding.UTF8);

                        // Duyệt xem media cần thêm có tồn tại trong playlist không
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (Path.GetFileName(line) == listView.FocusedItem.Text)
                            {
                                MessageBox.Show("The media already existed in \"" + args.ClickedItem.Text + "\"", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                available = true;  // Media đã tồn tại
                                break;
                            }
                        }

                        reader.Close();
                        stream.Close();
                    }
                    catch
                    {

                    }

                    // Nếu media cần thêm chưa tồn tại trong playlist
                    if (!available)
                    {
                        // Lấy đường dẫn của media đó
                        foreach (string item in dirMedia)
                        {
                            if (Path.GetFileName(item) == listView.FocusedItem.Text)
                            {
                                MessageBox.Show("Successfully !", "Notification", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                File.AppendAllText(@"Playlist\" + args.ClickedItem.Text + ".txt", item + Environment.NewLine);  // Ghi đường dẫn vào file .txt
                                break;
                            }
                        }
                    }
                };


                // Xem thông tin media
                iProperty.Click += (sender, args) =>
                {
                    foreach (string item in dirMedia)
                    {
                        if (Path.GetFileName(item) == listView.FocusedItem.Text)
                        {
                            FileInfo file = new FileInfo(item);
                            string title = file.Name.Substring(0, file.Name.Length - 4);  // Tên media
                            string duration = wmp.newMedia(item).durationString;  // Thời lượng
                            string directory = file.DirectoryName;  // Vị trí
                            float size = (float)file.Length / (1024 * 1024);  // Kích thước

                            MessageBox.Show("Title : " + title + "\nDuration : " + duration + "\nSize : " + String.Format("{0:0.0}", size) + " MB" + "\nLocation : " + directory, "Property", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            break;
                        }
                    }
                };
            }
            else if (type == "Playlist")  // Xử lý click phải cho lvPlaylist
            {
                context.Items.AddRange(new ToolStripItem[] { iRename, iDelete });

                // Đổi tên playlist
                iRename.Click += (sender, args) =>
                {
                    bool loop = false;  // Giải quyết lỗi AfterLabelEdit
                    listView.LabelEdit = true;  // Cho phép rename
                    listView.FocusedItem.BeginEdit();  // Bắt đầu rename
                    
                    // Sau khi rename
                    listView.AfterLabelEdit += (obj, evt) =>
                    {
                        if (!loop)  // Nếu loop = false <=> Chưa có lỗi loop
                        {
                            // Nếu tên mới rỗng
                            if (evt.Label == string.Empty)
                            {
                                MessageBox.Show("Please enter a name !", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                evt.CancelEdit = true;
                            }
                            // Nếu click rename mà chưa đặt tên hoặc tên mới đã có sẵn
                            else if (evt.Label != null && !Directory.GetFiles(@"Playlist", "*.txt").Contains(@"Playlist/" + evt.Label + ".txt"))
                            {
                                try
                                {
                                    // Rename
                                    File.Move(@"Playlist/" + listView.FocusedItem.Text + ".txt", @"Playlist/" + evt.Label + ".txt");
                                }
                                catch
                                {
                                    // Thông báo lỗi nếu không thể rename
                                    MessageBox.Show("The name already existed !", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                    evt.CancelEdit = true;
                                }
                            }
                        }

                        loop = true;
                    };
                };


                // Xóa playlist
                iDelete.Click += (sender, args) =>
                {
                    File.Delete(@"Playlist\" + listView.FocusedItem.Text + ".txt");  // Xóa file .txt của playlist
                    listView.Items.Remove(listView.FocusedItem);  // Xóa tên playlist khỏi lvPlaylist
                };
            }
            else  // Xử lý click phải cho lvLocation
            {
                context.Items.Add(iDelete);  // Thêm ToolStripItem delete

                // Xóa thư mục mặc định
                iDelete.Click += (sender, args) =>
                {
                    dirLocation.Remove(listView.FocusedItem.Text);  // Xóa đường dẫn thư mục khỏi dirLocation
                    listView.Items.Remove(listView.FocusedItem);  // Xóa tên thư mục khỏi lvLocation
                    File.Delete(@"Location.txt");  // Xóa file Location.txt

                    try
                    {
                        string line = string.Empty;
                        FileStream stream = new FileStream(@"Location.txt", FileMode.CreateNew);  // Tạo mới Location.txt
                        StreamWriter writer = new StreamWriter(stream, Encoding.UTF8);

                        foreach (string item in dirLocation)
                        {
                            writer.WriteLine(item);  // Ghi các đường dẫn đã update ở trên vào file Location.txt mới
                        }

                        writer.Close();
                        stream.Close();
                    }
                    catch
                    {

                    }
                };
            }
        }


/***************************************************************************************/


        // Phương thức xử lý nút Listen ở giao diện chính
        public void listen(Panel pListen)
        {
            pListen.Controls.Clear();
            pListen.BackColor = Color.Black;
            WMPLib.IWMPPlaylist playlist = wmp.newPlaylist(string.Empty, string.Empty);

            // Khung Playing chứa danh sách phát hoặc lyric
            GroupBox gbPlaying = new GroupBox();
            pListen.Controls.Add(gbPlaying);
            gbPlaying.Location = new Point(0, 12);
            gbPlaying.Size = new Size(454, 397);
            gbPlaying.ForeColor = Color.DeepSkyBlue;
            gbPlaying.Font = new Font("Times New Roman", 21, FontStyle.Bold, GraphicsUnit.Pixel);
            gbPlaying.Text = "My Music";
            
            RichTextBox rtbLyric = new RichTextBox();
            gbPlaying.Controls.Add(rtbLyric);
            rtbLyric.Dock = DockStyle.Fill;
            rtbLyric.BackColor = Color.Azure;
            rtbLyric.ForeColor = Color.Black;
            rtbLyric.Font = new Font("Times New Roman", 21, FontStyle.Regular, GraphicsUnit.Pixel);
            rtbLyric.Text = "Please wait a moment ...";
            rtbLyric.Visible = false;
            
            ListView lvPlaying = new ListView();
            gbPlaying.Controls.Add(lvPlaying);
            lvPlaying.Dock = DockStyle.Fill;
            lvPlaying.View = View.Tile;
            lvPlaying.MultiSelect = false;
            lvPlaying.BackgroundImage = Resources.PlayingWall;
            lvPlaying.BackgroundImageTiled = true;
            lvPlaying.ForeColor = Color.Black;
            lvPlaying.Font = new Font("Times New Roman", 21, FontStyle.Regular, GraphicsUnit.Pixel);
            

            // Load danh sách đang phát lên lvPlaying
            if (!wmp.status.Contains("Play"))  // Nếu bấm nút Listen lần đầu tiên => Play tất cả các media có sẵn
            {
                try
                {
                    string line = string.Empty;
                    FileStream stream = new FileStream(@"Location.txt", FileMode.Open);
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8);

                    while ((line = reader.ReadLine()) != null)
                    {
                        foreach (string item in Directory.GetFiles(line))
                        {
                            if (item.EndsWith(".mp3") || item.EndsWith(".wav") || item.EndsWith(".flac") || item.EndsWith(".mpg") || item.EndsWith(".mp4") || item.EndsWith(".mkv") || item.EndsWith(".vob"))
                            {
                                lvPlaying.Items.Add(Path.GetFileName(item));
                                WMPLib.IWMPMedia media = wmp.newMedia(item);
                                playlist.appendItem(media);
                                dirMedia.Add(item);
                            }
                        }
                    }

                    reader.Close();
                    stream.Close();
                }
                catch
                {

                }
                
                wmp.currentPlaylist = playlist;
            }
            else if(itemClicked != string.Empty)  // Hiển thị bài hát được click trong Search Result
            {
                gbPlaying.Text = "Now Playing";
                lvPlaying.Items.Add(itemClicked);
            }
            else  // Load tất cả các media đang phát lên lvPlaying từ dirMedia đã update từ hàm leftClick
            {
                foreach (string item in dirMedia)
                {
                    gbPlaying.Text = "Now Playing";
                    lvPlaying.Items.Add(Path.GetFileName(item));
                }
            }
                

            // Xử lý click chuột lên lvPlaying
            lvPlaying.MouseClick += (sender, args) =>
            {
                if (args.Button == MouseButtons.Left)
                    leftClick(lvPlaying, "Now Playing");
                else
                    rightClick(lvPlaying, rtbLyric, "Now Playing");
            };


            // Nút Back
            PictureBox pbBack = new PictureBox();
            pListen.Controls.Add(pbBack);
            pbBack.Location = new Point(468, 28);
            pbBack.Size = new Size(85, 85);
            pbBack.SizeMode = PictureBoxSizeMode.CenterImage;
            pbBack.BackColor = Color.Transparent;
            pbBack.Image = Resources.Back;

            // Zoom-in
            pbBack.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Back, pbBack, "Back");
            };

            // Zoom-out
            pbBack.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Back, pbBack);
            };

            // Click nút Back
            pbBack.Click += (sender, args) =>
            {
                if (rtbLyric.Visible == true)  // Nếu đang hiển thị lyric => Ẩn rtbLyric = Hiện lvPlaying ra
                {
                    rtbLyric.Visible = false;
                    rtbLyric.Text = "Please wait a moment ...";
                }
                else  // Trở về giao diện chính
                    pListen.SendToBack();
            };


            // Nút Player
            PictureBox pbPlayer = new PictureBox();
            pListen.Controls.Add(pbPlayer);
            pbPlayer.Location = new Point(475, 112);
            pbPlayer.Size = new Size(85, 85);
            pbPlayer.SizeMode = PictureBoxSizeMode.CenterImage;
            pbPlayer.BackColor = Color.Transparent;
            pbPlayer.Image = Resources.Player;
            
            // Zoom-in
            pbPlayer.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Player, pbPlayer, "Player");
            };
            
            // Zoom-out
            pbPlayer.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Player, pbPlayer);
            };

            // Click nút Player
            pbPlayer.Click += (sender, args) =>
            {
                wmp.BringToFront();  // Hiển thị Player
            };
            

            // Nút Open
            PictureBox pbOpen = new PictureBox();
            pListen.Controls.Add(pbOpen);
            pbOpen.Location = new Point(465, 212);
            pbOpen.Size = new Size(85, 85);
            pbOpen.SizeMode = PictureBoxSizeMode.CenterImage;
            pbOpen.BackColor = Color.Transparent;
            pbOpen.Image = Resources.Open;

            // Zoom-in
            pbOpen.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Open, pbOpen, "Open your music !");
            };

            // Zoom-out
            pbOpen.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Open, pbOpen);
            };

            // Click nút Open
            pbOpen.Click += (sender, args) =>
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "Media Files | *.mp3; *.wav; *.flac; *mpg; *.mp4; *.mkv; *.vob";
                ofd.Multiselect = true;

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    lvPlaying.Clear();
                    gbPlaying.Text = "Now Playing";
                    rtbLyric.Visible = false;
                    rtbLyric.Text = "Please wait a moment ...";
                    playlist.clear();
                    dirMedia.Clear();

                    foreach (string item in ofd.FileNames)
                    {
                        lvPlaying.Items.Add(Path.GetFileName(item));
                        WMPLib.IWMPMedia media = wmp.newMedia(item);
                        playlist.appendItem(media);
                        dirMedia.Add(item);
                    }

                    wmp.currentPlaylist = playlist;
                }
            };
            

            // Nút Home
            PictureBox pbHome = new PictureBox();
            pListen.Controls.Add(pbHome);
            pbHome.Location = new Point(468, 303);
            pbHome.Size = new Size(85, 85);
            pbHome.SizeMode = PictureBoxSizeMode.CenterImage;
            pbHome.BackColor = Color.Transparent;
            pbHome.Image = Resources.Home;

            // Zoom-in
            pbHome.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Home, pbHome, "Home");
            };

            // Zoom-out
            pbHome.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Home, pbHome);
            };

            // Click nút Home
            pbHome.Click += (sender, args) =>
            {
                pListen.SendToBack();
                rtbLyric.Visible = false;
            };
        }


/***************************************************************************************/


        // Phương thức xử lý nút Karaoke ở giao diện chính
        public void karaoke(Panel pKaraoke)
        {
            pKaraoke.BackColor = Color.Black;
            string status = "Karaoke";
            List<string> dirFavorite = new List<string>();

            // Ô tìm kiếm
            TextBox txtSearch = new TextBox();
            pKaraoke.Controls.Add(txtSearch);
            txtSearch.Location = new Point(18, 20);
            txtSearch.Size = new Size(300, 28);
            txtSearch.ForeColor = Color.Black;
            txtSearch.Font = new Font("Times New Roman", 20, FontStyle.Regular, GraphicsUnit.Pixel);

            // Icon tìm kiếm
            PictureBox btnSearch = new PictureBox();
            pKaraoke.Controls.Add(btnSearch);
            btnSearch.Location = new Point(324, 21);
            btnSearch.Size = new Size(28, 28);
            btnSearch.BackColor = Color.Transparent;
            btnSearch.Image = Resources.SearchButton;

            // Khung chứa kết quả tìm kiếm từ YouTube
            GroupBox gbKaraoke = new GroupBox();
            pKaraoke.Controls.Add(gbKaraoke);
            gbKaraoke.Location = new Point(0, 60);
            gbKaraoke.Size = new Size(454, 349);
            gbKaraoke.ForeColor = Color.DeepSkyBlue;
            gbKaraoke.Font = new Font("Times New Roman", 21, FontStyle.Bold, GraphicsUnit.Pixel);
            gbKaraoke.Text = "Karaoke";

            AxShockwaveFlashObjects.AxShockwaveFlash flash = new AxShockwaveFlashObjects.AxShockwaveFlash();
            gbKaraoke.Controls.Add(flash);
            flash.Dock = DockStyle.Fill;
            flash.AllowNetworking = "all";
            flash.AllowFullScreen = "true";
            flash.AllowScriptAccess = "always";
            flash.BackgroundColor = 0;
            flash.Visible = false;

            ListView lvKaraoke = new ListView();
            gbKaraoke.Controls.Add(lvKaraoke);
            lvKaraoke.Dock = DockStyle.Fill;
            lvKaraoke.View = View.Tile;
            lvKaraoke.MultiSelect = false;
            lvKaraoke.BackgroundImage = Resources.PlayingWall;
            lvKaraoke.BackgroundImageTiled = true;
            lvKaraoke.ForeColor = Color.Black;
            lvKaraoke.Font = new Font("Times New Roman", 21, FontStyle.Regular, GraphicsUnit.Pixel);


            try
            {
                string line = string.Empty;
                FileStream stream = new FileStream(@"Karaoke.txt", FileMode.Open);
                StreamReader reader = new StreamReader(stream, Encoding.UTF8);

                while ((line = reader.ReadLine()) != null)
                {
                    dirFavorite.Add(line);
                }

                reader.Close();
                stream.Close();
            }
            catch
            {

            }


            txtSearch.KeyDown += (sender, args) =>
            {
                if (args.KeyCode == Keys.Enter)
                {
                    HtmlWeb web = new HtmlWeb();
                    HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();

                    try
                    {
                        doc = web.Load("https://www.youtube.com/results?search_query=" + ("karaoke " + txtSearch.Text).Replace(" ", "+"));
                    }
                    catch
                    {
                        MessageBox.Show("There is no Internet connection", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                    getURL(lvKaraoke, doc);
                    gbKaraoke.Text = status = "Result";
                    flash.Visible = false;
                }
            };


            // Xử lý click chuột lên lvKaraoke
            lvKaraoke.MouseClick += (sender, args) =>
            {
                if (args.Button == MouseButtons.Left)
                {
                    if (gbKaraoke.Text == "Result")
                    {
                        wmp.Ctlcontrols.stop();
                        flash.Movie = urlYouTube[lvKaraoke.FocusedItem.Index];
                        flash.Visible = true;
                        gbKaraoke.Text = string.Empty;
                    }
                    else
                    {
                        foreach (string item in dirFavorite)
                        {
                            if (item == lvKaraoke.FocusedItem.Text)
                            {
                                wmp.Ctlcontrols.stop();
                                flash.Movie = dirFavorite[dirFavorite.IndexOf(item) + 1];
                                flash.Visible = true;
                                gbKaraoke.Text = string.Empty;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    if (gbKaraoke.Text == "Result")
                    {
                        ContextMenuStrip context = new ContextMenuStrip();
                        ToolStripItem iFavorite = new ToolStripMenuItem("Add to Favorite");
                        context.Items.Add(iFavorite);
                        lvKaraoke.ContextMenuStrip = context;

                        iFavorite.Click += (obj, evt) =>
                        {
                            bool available = false;

                            foreach (string item in dirFavorite)
                            {
                                if (item == urlYouTube[lvKaraoke.FocusedItem.Index])
                                {
                                    available = true;
                                    break;
                                }
                            }

                            if (!available)
                            {
                                MessageBox.Show("Successfully !", "Notification", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                File.AppendAllText(@"Karaoke.txt", lvKaraoke.FocusedItem.Text + Environment.NewLine + urlYouTube[lvKaraoke.FocusedItem.Index] + Environment.NewLine);
                                dirFavorite.Add(lvKaraoke.FocusedItem.Text);
                                dirFavorite.Add(urlYouTube[lvKaraoke.FocusedItem.Index]);
                            }
                            else
                                MessageBox.Show("The video already existed !", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        };
                    }
                    else
                    {
                        ContextMenuStrip context = new ContextMenuStrip();
                        ToolStripItem iDelete = new ToolStripMenuItem("Delete");
                        context.Items.Add(iDelete);
                        lvKaraoke.ContextMenuStrip = context;

                        iDelete.Click += (obj, evt) =>
                        {
                            // Xóa Title + URL cần xóa
                            foreach (string item in dirFavorite)
                            {
                                if (item == lvKaraoke.FocusedItem.Text)
                                {
                                    dirFavorite.RemoveRange(dirFavorite.IndexOf(item), 2);
                                    break;
                                }
                            }

                            File.Delete(@"Karaoke.txt");  // Xóa file Karaoke.txt

                            try
                            {
                                // Tạo mới file Karaoke.txt
                                FileStream stream = new FileStream(@"Karaoke.txt", FileMode.CreateNew);
                                StreamWriter writer = new StreamWriter(stream, Encoding.UTF8);

                                foreach (string item in dirFavorite)
                                {
                                    writer.WriteLine(item);  // Ghi các đường dẫn Favorite đã update
                                }

                                writer.Close();
                                stream.Close();
                            }
                            catch
                            {

                            }

                            lvKaraoke.Items.Remove(lvKaraoke.FocusedItem);  // Xóa tên video khỏi lvKaraoke
                        };
                    }
                }
            };


            // Nút Back
            PictureBox pbBack = new PictureBox();
            pKaraoke.Controls.Add(pbBack);
            pbBack.Location = new Point(466, 28);
            pbBack.Size = new Size(85, 85);
            pbBack.SizeMode = PictureBoxSizeMode.CenterImage;
            pbBack.BackColor = Color.Transparent;
            pbBack.Image = Resources.Back;

            // Zoom-in
            pbBack.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Back, pbBack, "Back");
            };

            // Zoom-out
            pbBack.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Back, pbBack);
            };

            // Click nút Back
            pbBack.Click += (sender, args) =>
            {
                if (flash.Visible)
                    flash.Visible = false;
                else if(gbKaraoke.Text == "Favorite" && txtSearch.Text.Trim() != string.Empty)
                {
                    HtmlWeb web = new HtmlWeb();
                    HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();

                    try
                    {
                        doc = web.Load("https://www.youtube.com/results?search_query=" + ("karaoke " + txtSearch.Text).Replace(" ", "+"));
                    }
                    catch
                    {
                        MessageBox.Show("There is no Internet connection", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                    getURL(lvKaraoke, doc);
                    gbKaraoke.Text = status = "Result";
                }
                else
                {
                    pKaraoke.SendToBack();
                    flash.Visible = false;
                    flash.Movie = "Karaoke";
                }
                
                gbKaraoke.Text = status;
            };


            // Nút Player
            PictureBox pbPlayer = new PictureBox();
            pKaraoke.Controls.Add(pbPlayer);
            pbPlayer.Location = new Point(475, 114);
            pbPlayer.Size = new Size(85, 85);
            pbPlayer.SizeMode = PictureBoxSizeMode.CenterImage;
            pbPlayer.BackColor = Color.Transparent;
            pbPlayer.Image = Resources.Player;

            // Zoom-in
            pbPlayer.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Player, pbPlayer, "Player");
            };

            // Zoom-out
            pbPlayer.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Player, pbPlayer);
            };

            // Click nút Player
            pbPlayer.Click += (sender, args) =>
            {
                if(flash.Movie != null && flash.Movie != "Karaoke")
                {
                    flash.Visible = true;
                    gbKaraoke.Text = string.Empty;
                }
            };


            // Nút Favorite
            PictureBox pbFavorite = new PictureBox();
            pKaraoke.Controls.Add(pbFavorite);
            pbFavorite.Location = new Point(468, 216);
            pbFavorite.Size = new Size(85, 85);
            pbFavorite.SizeMode = PictureBoxSizeMode.CenterImage;
            pbFavorite.BackColor = Color.Transparent;
            pbFavorite.Image = Resources.Favorite;

            // Zoom-in
            pbFavorite.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Favorite, pbFavorite, "Favorite");
            };

            // Zoom-out
            pbFavorite.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Favorite, pbFavorite);
            };

            // Click nút Favorite
            pbFavorite.Click += (sender, args) =>
            {
                lvKaraoke.Clear();
                flash.Visible = false;
                gbKaraoke.Text = status = "Favorite";

                int i = 0;

                foreach (string item in dirFavorite)
                {
                    if (i % 2 == 0)
                        lvKaraoke.Items.Add(item);

                    i++;
                }
            };


            // Nút Home
            PictureBox pbHome = new PictureBox();
            pKaraoke.Controls.Add(pbHome);
            pbHome.Location = new Point(468, 303);
            pbHome.Size = new Size(85, 85);
            pbHome.SizeMode = PictureBoxSizeMode.CenterImage;
            pbHome.BackColor = Color.Transparent;
            pbHome.Image = Resources.Home;

            // Zoom-in
            pbHome.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Home, pbHome, "Home");
            };

            // Zoom-out
            pbHome.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Home, pbHome);
            };

            // Click nút Home
            pbHome.Click += (sender, args) =>
            {
                pKaraoke.SendToBack();
                flash.Visible = false;
                flash.Movie = "Karaoke";
                gbKaraoke.Text = status;
            };
        }


/***************************************************************************************/


        // Phương thức xử lý nút Playlist ở giao diện chính
        public void playlist(Panel pPlaylist)
        {
            pPlaylist.BackColor = Color.Black;
            WMPLib.IWMPPlaylist playlist = wmp.newPlaylist(string.Empty, string.Empty);
            
            // Khung chứa danh sách các Playlist
            GroupBox gbPlaylist = new GroupBox();
            pPlaylist.Controls.Add(gbPlaylist);
            gbPlaylist.Location = new Point(0, 12);
            gbPlaylist.Size = new Size(160, 397);
            gbPlaylist.ForeColor = Color.DeepSkyBlue;
            gbPlaylist.Font = new Font("Times New Roman", 21, FontStyle.Bold, GraphicsUnit.Pixel);
            gbPlaylist.Text = "Playlist";

            ListView lvPlaylist = new ListView();
            gbPlaylist.Controls.Add(lvPlaylist);
            lvPlaylist.Dock = DockStyle.Fill;
            lvPlaylist.View = View.SmallIcon;
            lvPlaylist.MultiSelect = false;
            lvPlaylist.BorderStyle = BorderStyle.None;
            lvPlaylist.BackColor = Color.Black;
            lvPlaylist.ForeColor = Color.Lime;
            lvPlaylist.Font = new Font("Times New Roman", 21, FontStyle.Bold, GraphicsUnit.Pixel);

            // Khung chứa danh sách các media đang phát
            GroupBox gbPlaying = new GroupBox();
            pPlaylist.Controls.Add(gbPlaying);
            gbPlaying.Location = new Point(159, 12);
            gbPlaying.Size = new Size(295, 397);
            gbPlaying.ForeColor = Color.DeepSkyBlue;
            gbPlaying.Font = new Font("Times New Roman", 21, FontStyle.Bold, GraphicsUnit.Pixel);
            gbPlaying.Text = "Your Music";

            RichTextBox rtbLyric = new RichTextBox();
            gbPlaying.Controls.Add(rtbLyric);
            rtbLyric.Dock = DockStyle.Fill;
            rtbLyric.BackColor = Color.Azure;
            rtbLyric.ForeColor = Color.Black;
            rtbLyric.Font = new Font("Times New Roman", 21, FontStyle.Regular, GraphicsUnit.Pixel);
            rtbLyric.Text = "Please wait a moment ...";
            rtbLyric.Visible = false;

            ListView lvPlaying = new ListView();
            gbPlaying.Controls.Add(lvPlaying);
            lvPlaying.Dock = DockStyle.Fill;
            lvPlaying.View = View.Tile;
            lvPlaying.MultiSelect = false;
            lvPlaying.BackgroundImage = Resources.PlayingWall;
            lvPlaying.BackgroundImageTiled = true;
            lvPlaying.ForeColor = Color.Black;
            lvPlaying.Font = new Font("Times New Roman", 21, FontStyle.Regular, GraphicsUnit.Pixel);
            

            // Load tất cả các playlist lên khung Playlist lần đầu tiên
            try
            {
                foreach (string item in Directory.GetFiles(@"Playlist", "*.txt"))
                {
                    if (Path.GetFileNameWithoutExtension(item) != string.Empty)
                        lvPlaylist.Items.Add(Path.GetFileNameWithoutExtension(item));
                    else
                        File.Delete(item);
                }
            }
            catch
            {
                
            }

            
            // Xử lý click chuột lên lvPlaylist
            lvPlaylist.MouseClick += (sender, args) =>
            {
                if (args.Button == MouseButtons.Left)  // Click trái
                {
                    lvPlaying.Clear();
                    gbPlaying.Text = lvPlaylist.FocusedItem.Text;
                    leftClick(lvPlaylist, "Playlist");
                    playlist.clear();
                    rtbLyric.Visible = false;
                    rtbLyric.Text = "Please wait a moment ...";
                    dirPlaylist = lvPlaylist.FocusedItem.Text;
                    itemClicked = string.Empty;
                    found = false;

                    // Hiển thị các media trong playlist được chọn lên lvPlaying từ dirMedia
                    // dirMedia đã được update bằng hàm leftClick
                    foreach (string item in dirMedia)
                    {
                        lvPlaying.Items.Add(Path.GetFileName(item));
                        WMPLib.IWMPMedia media = wmp.newMedia(item);
                        playlist.appendItem(media);
                    }

                    wmp.currentPlaylist = playlist;
                }
                else  // Click phải (Xóa playlist được chọn)
                    rightClick(lvPlaylist, rtbLyric, "Playlist");
            };


            // Xử lý click chuột lên lvPlaying
            lvPlaying.MouseClick += (sender, args) =>
            {
                if (args.Button == MouseButtons.Left)  // Click trái => Play media được chọn
                    leftClick(lvPlaying, "Now Playing");
                else  // Click phải => Xem lyric, property, xóa
                    rightClick(lvPlaying, rtbLyric, "Now Playing");
            };


            // Nút Back
            PictureBox pbBack = new PictureBox();
            pPlaylist.Controls.Add(pbBack);
            pbBack.Location = new Point(467, 28);
            pbBack.Size = new Size(85, 85);
            pbBack.SizeMode = PictureBoxSizeMode.CenterImage;
            pbBack.BackColor = Color.Transparent;
            pbBack.Image = Resources.Back;

            // Zoom-in
            pbBack.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Back, pbBack, "Back");
            };

            // Zoom-out
            pbBack.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Back, pbBack);
            };

            // Click nút Back
            pbBack.Click += (sender, args) =>
            {
                if (rtbLyric.Visible == true)
                {
                    rtbLyric.Visible = false;
                    rtbLyric.Text = "Please wait a moment ...";
                }
                else
                    pPlaylist.SendToBack();
            };


            // Nút Player
            PictureBox pbPlayer = new PictureBox();
            pPlaylist.Controls.Add(pbPlayer);
            pbPlayer.Location = new Point(475, 116);
            pbPlayer.Size = new Size(85, 85);
            pbPlayer.SizeMode = PictureBoxSizeMode.CenterImage;
            pbPlayer.BackColor = Color.Transparent;
            pbPlayer.Image = Resources.Player;

            // Zoom-in
            pbPlayer.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Player, pbPlayer, "Player");
            };

            // Zoom-out
            pbPlayer.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Player, pbPlayer);
            };

            // Click nút Player
            pbPlayer.Click += (sender, args) =>
            {
                wmp.BringToFront();
            };
            

            // Nút NewPlaylist
            PictureBox pbNewPlaylist = new PictureBox();
            pPlaylist.Controls.Add(pbNewPlaylist);
            pbNewPlaylist.Location = new Point(467, 215);
            pbNewPlaylist.Size = new Size(85, 85);
            pbNewPlaylist.SizeMode = PictureBoxSizeMode.CenterImage;
            pbNewPlaylist.BackColor = Color.Transparent;
            pbNewPlaylist.Image = Resources.NewPlaylist;

            // Zoom-in
            pbNewPlaylist.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.NewPlaylist, pbNewPlaylist, "New Playlist");
            };

            // Zoom-out
            pbNewPlaylist.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.NewPlaylist, pbNewPlaylist);
            };

            // Click nút NewPlaylist
            pbNewPlaylist.Click += (sender, args) =>
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "Media Files | *.mp3; *.wav; *.flac; *mpg; *.mp4; *.mkv; *.vob";
                ofd.Multiselect = true;

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    Retry:  // Hiển thị form rename nếu tên playlist đã tồn tại

                    Playlist playlistName = new Playlist();  // Mở form điền tên playlist đang tạo

                    if (playlistName.ShowDialog() == DialogResult.OK)
                    {
                        try
                        {
                            FileStream stream = new FileStream(@"Playlist\" + playlistName.getName() + ".txt", FileMode.CreateNew);
                            StreamWriter writer = new StreamWriter(stream, Encoding.UTF8);

                            foreach (string item in ofd.FileNames)
                            {
                                writer.WriteLine(item);  // Lưu danh sách bài hát vào file (Tên playlist).txt
                            }

                            writer.Close();
                            stream.Close();

                            lvPlaylist.Items.Add(playlistName.getName());  // Hiển thị playlist mới tạo lên khung lvPlaylist
                            MessageBox.Show("\"" + playlistName.getName() + "\"" + " is successfully created !", "Notification", MessageBoxButtons.OK, MessageBoxIcon.Information);  // Thông báo tạo mới thành công
                        }
                        catch
                        {
                            // Thông báo tên playlist đã tồn tại => Nhập một tên khác
                            DialogResult result = MessageBox.Show("The playlist might already exist\nPlease enter an other name !", "Error", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);

                            if(result == DialogResult.Retry)
                                goto Retry;  // Mở form điền tên playlist mới
                        }
                    }
                }
            };


            // Nút Home
            PictureBox pbHome = new PictureBox();
            pPlaylist.Controls.Add(pbHome);
            pbHome.Location = new Point(467, 303);
            pbHome.Size = new Size(85, 85);
            pbHome.SizeMode = PictureBoxSizeMode.CenterImage;
            pbHome.BackColor = Color.Transparent;
            pbHome.Image = Resources.Home;

            // Zoom-in
            pbHome.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Home, pbHome, "Home");
            };

            // Zoom-out
            pbHome.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Home, pbHome);
            };

            // Click nút Home
            pbHome.Click += (sender, args) =>
            {
                pPlaylist.SendToBack();
                rtbLyric.Visible = false;
                rtbLyric.Text = "Please wait a moment ...";
            };
        }


/***************************************************************************************/


        // Phương thức xử lý nút Search trong giao diện chính
        public void search(Panel pSearch)
        {
            dirLocation.Clear();
            pSearch.BackColor = Color.Black;
            WMPLib.IWMPPlaylist playlist = wmp.newPlaylist(string.Empty, string.Empty);

            // Ô tìm kiếm
            TextBox txtSearch = new TextBox();
            pSearch.Controls.Add(txtSearch);
            txtSearch.Location = new Point(18, 20);
            txtSearch.Size = new Size(300, 28);
            txtSearch.ForeColor = Color.Black;
            txtSearch.Font = new Font("Times New Roman", 20, FontStyle.Regular, GraphicsUnit.Pixel);

            // Icon tìm kiếm
            PictureBox btnSearch = new PictureBox();
            pSearch.Controls.Add(btnSearch);
            btnSearch.Location = new Point(324, 21);
            btnSearch.Size = new Size(28, 28);
            btnSearch.BackColor = Color.Transparent;
            btnSearch.Image = Resources.SearchButton;
            
            // Khung hiển thị các thư mục mặc định chứa media
            GroupBox gbLocation = new GroupBox();
            pSearch.Controls.Add(gbLocation);
            gbLocation.Location = new Point(0, 62);
            gbLocation.Size = new Size(160, 347);
            gbLocation.ForeColor = Color.DeepSkyBlue;
            gbLocation.Font = new Font("Times New Roman", 21, FontStyle.Bold, GraphicsUnit.Pixel);
            gbLocation.Text = "Music Folder";

            ListView lvLocation = new ListView();
            gbLocation.Controls.Add(lvLocation);
            lvLocation.Dock = DockStyle.Fill;
            lvLocation.View = View.SmallIcon;
            lvLocation.MultiSelect = false;
            lvLocation.BorderStyle = BorderStyle.None;
            lvLocation.BackColor = Color.Black;
            lvLocation.ForeColor = Color.Lime;
            lvLocation.Font = new Font("Times New Roman", 21, FontStyle.Bold, GraphicsUnit.Pixel);
            
            // Khung hiển thị kết quả tìm kiếm
            GroupBox gbSearch = new GroupBox();
            pSearch.Controls.Add(gbSearch);
            gbSearch.Location = new Point(159, 62);
            gbSearch.Size = new Size(296, 347);
            gbSearch.ForeColor = Color.DeepSkyBlue;
            gbSearch.Font = new Font("Times New Roman", 21, FontStyle.Bold, GraphicsUnit.Pixel);
            gbSearch.Text = "Search Result";

            RichTextBox rtbLyric = new RichTextBox();
            gbSearch.Controls.Add(rtbLyric);
            rtbLyric.Dock = DockStyle.Fill;
            rtbLyric.BackColor = Color.Azure;
            rtbLyric.ForeColor = Color.Black;
            rtbLyric.Font = new Font("Times New Roman", 21, FontStyle.Regular, GraphicsUnit.Pixel);
            rtbLyric.Text = "Please wait a moment ...";
            rtbLyric.Visible = false;

            ListView lvSearch = new ListView();
            gbSearch.Controls.Add(lvSearch);
            lvSearch.Dock = DockStyle.Fill;
            lvSearch.View = View.Tile;
            lvSearch.MultiSelect = false;
            lvSearch.BackgroundImage = Resources.PlayingWall;
            lvSearch.BackgroundImageTiled = true;
            lvSearch.ForeColor = Color.Black;
            lvSearch.Font = new Font("Times New Roman", 21, FontStyle.Regular, GraphicsUnit.Pixel);


            // Load tất cả các thư mục mặc định chứa media lên lvLocation
            try
            {
                string line = string.Empty;
                FileStream stream = new FileStream(@"Location.txt", FileMode.Open);
                StreamReader reader = new StreamReader(stream, Encoding.UTF8);

                while ((line = reader.ReadLine()) != null)
                {
                    lvLocation.Items.Add(line);
                    dirLocation.Add(line);  // Lưu tất cả các đường dẫn thư mục chứa media vào dirLocation
                }

                reader.Close();
                stream.Close();
            }
            catch
            {

            }


            // Hiển thị kết quả tìm kiếm lên lvSearch khi gõ từ khóa
            txtSearch.TextChanged += (sender, args) =>
            {
                if (lvLocation.Items.Count == 0)  // Yêu cầu chọn thư mục chứa các media cần tìm
                    MessageBox.Show("Please choose where to look !", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                {
                    lvSearch.Clear();
                    gbSearch.Text = "Search Result";
                    rtbLyric.Visible = false;
                    rtbLyric.Text = "Please wait a moment ...";
                    dirMedia.Clear();
                    found = false;

                    // Duyệt tất cả đường dẫn thư mục mặc định trong dirLocation
                    foreach (string dir in dirLocation)
                    {
                        foreach (string item in Directory.GetFiles(dir))  // Lấy tất cả các file là media trong từng thư mục mặc định
                        {
                            if (item.EndsWith(".mp3") || item.EndsWith(".wav") || item.EndsWith(".flac") || item.EndsWith(".mpg") || item.EndsWith(".mp4") || item.EndsWith(".mkv") || item.EndsWith(".vob"))
                                dirMedia.Add(item);  // Lưu tất cả đường dẫn media dùng cho tìm kiếm
                        }
                    }

                    // Nếu từ khóa trong ô rtbSearch phù hợp => Hiển thị các media liên quan lên lvSearch
                    foreach (string item in dirMedia)
                    {
                        // Kiểm tra từ khóa tìm kiếm hợp lệ hay không
                        if (txtSearch.Text.Trim() != string.Empty && convertText(Path.GetFileNameWithoutExtension(item)).ToLower().Trim().Replace("-", " ").Replace("+", " ").Replace("  ", " ").Contains(convertText(txtSearch.Text).ToLower().Trim().Replace("  ", " ")))
                        {
                            lvSearch.Items.Add(Path.GetFileName(item));
                            found = true;
                        }
                    }

                    // Thông báo nếu không có kết quả phù hợp
                    if (found == false)
                        lvSearch.Items.Add("Not found !");
                }
            };


            // Xử lý click chuột lên lvLocation
            lvLocation.MouseClick += (sender, args) =>
            {
                if (args.Button == MouseButtons.Left)  // Click trái => Hiển thị các media trong thư mục được chọn
                {
                    lvSearch.Clear();
                    gbSearch.Text = Path.GetFileName(lvLocation.FocusedItem.Text);
                    leftClick(lvLocation, "Location");
                    playlist.clear();
                    rtbLyric.Visible = false;
                    rtbLyric.Text = "Please wait a moment ...";
                    itemClicked = string.Empty;
                    found = false;

                    // dirMedia lấy từ hàm leftClick
                    foreach (string item in dirMedia)
                    {
                        lvSearch.Items.Add(Path.GetFileName(item));
                        WMPLib.IWMPMedia media = wmp.newMedia(item);
                        playlist.appendItem(media);
                    }

                    wmp.currentPlaylist = playlist;
                }
                else  // Click phải => Xóa thư mục mặc định được chọn
                    rightClick(lvLocation, rtbLyric, "Location");
            };
            
            
            // Xử lý click chuột lên lvSearch
            lvSearch.MouseClick += (sender, args) =>
            {
                if (args.Button == MouseButtons.Left)  // Click trái => Phát media được chọn
                    leftClick(lvSearch, "Now Playing");
                else  // Click phải => Xem lyric, property, xóa
                    rightClick(lvSearch, rtbLyric, "Now Playing");
            };


            // Nút Back
            PictureBox pbBack = new PictureBox();
            pSearch.Controls.Add(pbBack);
            pbBack.Location = new Point(468, 28);
            pbBack.Size = new Size(85, 85);
            pbBack.SizeMode = PictureBoxSizeMode.CenterImage;
            pbBack.BackColor = Color.Transparent;
            pbBack.Image = Resources.Back;

            // Zoom-in
            pbBack.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Back, pbBack, "Back");
            };

            // Zoom-out
            pbBack.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Back, pbBack);
            };

            // Click nút Back
            pbBack.Click += (sender, args) =>
            {
                if (rtbLyric.Visible == true)  // Nếu rtbLyric đang hiển thị => Trở về xem lvSearch
                {
                    rtbLyric.Visible = false;
                    rtbLyric.Text = "Please wait a moment ...";
                }
                else if(gbSearch.Text == "Search Result")  // Nếu đang hiển thị kết quả tìm kiếm => Xóa
                {
                    pSearch.SendToBack();
                    txtSearch.Text = string.Empty;
                    lvSearch.Clear();
                }
                else
                    pSearch.SendToBack();  // Hiển thị giao diện chính
            };


            // Nút Player
            PictureBox pbPlayer = new PictureBox();
            pSearch.Controls.Add(pbPlayer);
            pbPlayer.Location = new Point(475, 114);
            pbPlayer.Size = new Size(85, 85);
            pbPlayer.SizeMode = PictureBoxSizeMode.CenterImage;
            pbPlayer.BackColor = Color.Transparent;
            pbPlayer.Image = Resources.Player;

            // Zoom-in
            pbPlayer.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Player, pbPlayer, "Player");
            };

            // Zoom-out
            pbPlayer.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Player, pbPlayer);
            };

            // Click nút Player
            pbPlayer.Click += (sender, args) =>
            {
                wmp.BringToFront();
            };


            // Nút Location
            PictureBox pbLocation = new PictureBox();
            pSearch.Controls.Add(pbLocation);
            pbLocation.Location = new Point(468, 216);
            pbLocation.Size = new Size(85, 85);
            pbLocation.SizeMode = PictureBoxSizeMode.CenterImage;
            pbLocation.BackColor = Color.Transparent;
            pbLocation.Image = Resources.Location;

            // Zoom-in
            pbLocation.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Location, pbLocation, "Choose where to look !");
            };

            // Zoom-out
            pbLocation.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Location, pbLocation);
            };

            // Click nút Location
            pbLocation.Click += (sender, args) =>
            {
                bool avaiable = false;
                FolderBrowserDialog fbd = new FolderBrowserDialog();

                try  // try/catch => Tránh lỗi thoát Dialog
                {
                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        foreach (string item in dirLocation)  // dirLocation chứa tất cả các thư mục mặc định hiện có sẵn
                        {
                            if (item == fbd.SelectedPath)  // Nếu thư mục đang chọn đã tồn tại => Cảnh báo
                            {
                                MessageBox.Show("The folder you selected already existed !", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                avaiable = true;
                                break;
                            }
                        }
                    }


                    // Nếu thư mục đang chọn chưa tồn tại
                    if (!avaiable)
                    {
                        // Kiểm tra thư mục đang chọn có chứa media không
                        foreach (string item in Directory.GetFiles(fbd.SelectedPath))
                        {
                            if (item.EndsWith(".mp3") || item.EndsWith(".wav") || item.EndsWith(".flac") || item.EndsWith(".mpg") || item.EndsWith(".mp4") || item.EndsWith(".mkv") || item.EndsWith(".vob"))
                            {
                                avaiable = false;
                                break;
                            }
                            else
                                avaiable = true;
                        }

                        // Nếu đường dẫn đã chọn có chứa media => Lưu mới
                        if (!avaiable)
                        {
                            lvLocation.Items.Add(fbd.SelectedPath);
                            File.AppendAllText(@"Location.txt", fbd.SelectedPath + Environment.NewLine);
                            dirLocation.Add(fbd.SelectedPath);
                        }
                        else  // Thông báo thư mục không có media
                            MessageBox.Show("The folder doesn't contain media files !", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch
                {

                }
            };


            // Nút Home
            PictureBox pbHome = new PictureBox();
            pSearch.Controls.Add(pbHome);
            pbHome.Location = new Point(468, 303);
            pbHome.Size = new Size(85, 85);
            pbHome.SizeMode = PictureBoxSizeMode.CenterImage;
            pbHome.BackColor = Color.Transparent;
            pbHome.Image = Resources.Home;

            // Zoom-in
            pbHome.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Home, pbHome, "Home");
            };

            // Zoom-out
            pbHome.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Home, pbHome);
            };

            // Click nút Home
            pbHome.Click += (sender, args) =>
            {
                pSearch.SendToBack();
                rtbLyric.Visible = false;
                rtbLyric.Text = "Please wait a moment ...";

                // Xóa từ khóa và kết quả tìm kiếm
                if (gbSearch.Text == "Search Result")
                {
                    txtSearch.Text = string.Empty;
                    lvSearch.Clear();
                }
            };
        }


/***************************************************************************************/


        public void power(Form form, Panel pPower)
        {
            int time = 0;
            Timer timer = new Timer();
            timer.Interval = 1000;
            string type = string.Empty;
            pPower.BackColor = Color.Black;

            // Khung Timer
            GroupBox gbTimer = new GroupBox();
            pPower.Controls.Add(gbTimer);
            gbTimer.Location = new Point(0, 12);
            gbTimer.Size = new Size(455, 397);
            gbTimer.ForeColor = Color.Lime;
            gbTimer.Font = new Font("Times New Roman", 21, FontStyle.Bold, GraphicsUnit.Pixel);
            gbTimer.Text = "Music Timer";

            // Label chủ đề
            Label lblTitle = new Label();
            gbTimer.Controls.Add(lblTitle);
            lblTitle.Location = new Point(119, 46);
            lblTitle.Width = 250;
            lblTitle.ForeColor = Color.Blue;
            lblTitle.Font = new Font("Lucida Bright", 24, FontStyle.Bold, GraphicsUnit.Pixel);
            lblTitle.Text = "Select Your Timer";

            // Hour
            ComboBox cbHour = new ComboBox();
            gbTimer.Controls.Add(cbHour);
            cbHour.FlatStyle = FlatStyle.Popup;
            cbHour.DropDownStyle = ComboBoxStyle.DropDownList;
            cbHour.Location = new Point(28, 102);
            cbHour.Width = 50;
            cbHour.ForeColor = Color.Lime;
            cbHour.Font = new Font("Times New Roman", 18, FontStyle.Regular, GraphicsUnit.Pixel);

            Label lblHour = new Label();
            gbTimer.Controls.Add(lblHour);
            lblHour.Location = new Point(80, 106);
            lblHour.Width = 80;
            lblHour.ForeColor = Color.Red;
            lblHour.Font = new Font("Lucida Bright", 18, FontStyle.Regular, GraphicsUnit.Pixel);
            lblHour.Text = "Hour";

            // Minute
            ComboBox cbMinute = new ComboBox();
            gbTimer.Controls.Add(cbMinute);
            cbMinute.FlatStyle = FlatStyle.Popup;
            cbMinute.DropDownStyle = ComboBoxStyle.DropDownList;
            cbMinute.Location = new Point(161, 102);
            cbMinute.Width = 50;
            cbMinute.ForeColor = Color.Lime;
            cbMinute.Font = new Font("Times New Roman", 18, FontStyle.Regular, GraphicsUnit.Pixel);

            Label lblMinute = new Label();
            gbTimer.Controls.Add(lblMinute);
            lblMinute.Location = new Point(213, 106);
            lblMinute.Width = 80;
            lblMinute.ForeColor = Color.Red;
            lblMinute.Font = new Font("Lucida Bright", 18, FontStyle.Regular, GraphicsUnit.Pixel);
            lblMinute.Text = "Minute";
            
            // Second
            ComboBox cbSecond = new ComboBox();
            gbTimer.Controls.Add(cbSecond);
            cbSecond.FlatStyle = FlatStyle.Popup;
            cbSecond.DropDownStyle = ComboBoxStyle.DropDownList;
            cbSecond.Location = new Point(310, 102);
            cbSecond.Width = 50;
            cbSecond.ForeColor = Color.Lime;
            cbSecond.Font = new Font("Times New Roman", 18, FontStyle.Regular, GraphicsUnit.Pixel);

            Label lblSecond = new Label();
            gbTimer.Controls.Add(lblSecond);
            lblSecond.Location = new Point(362, 106);
            lblSecond.Width = 80;
            lblSecond.ForeColor = Color.Red;
            lblSecond.Font = new Font("Lucida Bright", 18, FontStyle.Regular, GraphicsUnit.Pixel);
            lblSecond.Text = "Second";
            
            // Thêm item cho 3 ComboBox
            for (int i = 0; i < 13; i++)
            {
                cbHour.Items.Add(i);
                cbMinute.Items.Add(i);
                cbSecond.Items.Add(i);
            }

            for (int i = 13; i < 60; i++)
            {
                cbMinute.Items.Add(i);
                cbSecond.Items.Add(i);
            }

            // Set giá trị mặc định
            cbHour.Text = "0";
            cbMinute.Text = "30";
            cbSecond.Text = "0";

            // Label hiển thị thời gian còn lại
            Label lblRemain = new Label();
            gbTimer.Controls.Add(lblRemain);
            lblRemain.Location = new Point(46, 165);
            lblRemain.Width = 270;
            lblRemain.ForeColor = Color.Yellow;

            
            // Nút Close đóng ứng dụng
            Button btnClose = new Button();
            gbTimer.Controls.Add(btnClose);
            btnClose.Location = new Point(49, 200);
            btnClose.Size = new Size(114, 38);
            btnClose.ForeColor = Color.Lime;
            btnClose.Text = "Close";

            btnClose.Click += (sender, args) =>
            {
                time = 3600 * int.Parse(cbHour.SelectedItem.ToString()) + 60 * int.Parse(cbMinute.SelectedItem.ToString()) + int.Parse(cbSecond.SelectedItem.ToString());
                timer.Start();
                type = "Close";
            };


            // Nút Sleep
            Button btnSleep = new Button();
            gbTimer.Controls.Add(btnSleep);
            btnSleep.Location = new Point(169, 200);
            btnSleep.Size = new Size(114, 38);
            btnSleep.ForeColor = Color.Lime;
            btnSleep.Text = "Sleep";

            btnSleep.Click += (sender, args) =>
            {
                time = 3600 * int.Parse(cbHour.SelectedItem.ToString()) + 60 * int.Parse(cbMinute.SelectedItem.ToString()) + int.Parse(cbSecond.SelectedItem.ToString());
                timer.Start();
                type = "Sleep";
            };


            // Nút Shutdown
            Button btnShutdown = new Button();
            gbTimer.Controls.Add(btnShutdown);
            btnShutdown.Location = new Point(289, 200);
            btnShutdown.Size = new Size(114, 38);
            btnShutdown.ForeColor = Color.Lime;
            btnShutdown.Text = "Shutdown";

            btnShutdown.Click += (sender, args) =>
            {
                time = 3600 * int.Parse(cbHour.SelectedItem.ToString()) + 60 * int.Parse(cbMinute.SelectedItem.ToString()) + int.Parse(cbSecond.SelectedItem.ToString());
                timer.Start();
                type = "Shutdown";
            };


            // Nút Pause
            Button btnPause = new Button();
            gbTimer.Controls.Add(btnPause);
            btnPause.Location = new Point(49, 245);
            btnPause.Size = new Size(114, 38);
            btnPause.ForeColor = Color.Lime;
            btnPause.Text = "Pause";
            btnPause.Enabled = false;
            
            // Nút Resume
            Button btnResume = new Button();
            gbTimer.Controls.Add(btnResume);
            btnResume.Location = new Point(169, 245);
            btnResume.Size = new Size(114, 38);
            btnResume.ForeColor = Color.Lime;
            btnResume.Text = "Resume";
            btnResume.Enabled = false;

            // Nút Cancel
            Button btnCancel = new Button();
            gbTimer.Controls.Add(btnCancel);
            btnCancel.Location = new Point(289, 245);
            btnCancel.Size = new Size(114, 38);
            btnCancel.ForeColor = Color.Lime;
            btnCancel.Text = "Cancel";
            btnCancel.Enabled = false;

            // Click nút Pause
            btnPause.Click += (sender, args) =>
            {
                timer.Stop();
                btnResume.Enabled = true;
                btnPause.Enabled = false;
            };

            // Click nút Resume
            btnResume.Click += (sender, args) =>
            {
                timer.Start();
                btnPause.Enabled = true;
                btnResume.Enabled = false;
            };

            //Click nút Cancel
            btnCancel.Click += (sender, args) =>
            {
                timer.Stop();
                btnPause.Enabled = false;
                btnResume.Enabled = false;
                btnCancel.Enabled = false;
                lblRemain.Text = "Canceled !";
            };


            // Timer Tick
            timer.Tick += (sender, args) =>
            {
                btnPause.Enabled = true;
                btnCancel.Enabled = true;
                
                int hour = time / 3600;
                int minute = (time % 3600) / 60;
                int second = time - (3600 * hour + 60 * minute);

                lblRemain.Text = type + " after " + hour.ToString("00") + " : " + minute.ToString("00") + " : " + second.ToString("00"); ;
                
                time--;

                // Nhắc nhở shutdown trước 5 phút
                if(time - 300 == -1 && type == "Shutdown")
                {
                    DialogResult dialog = MessageBox.Show("Your PC will shutdown after 5 minutes\nAre you sure ?", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    
                    if (dialog == DialogResult.No)
                    {
                        lblRemain.Text = "Canceled !";
                        timer.Stop();
                    }

                    // Nếu không chọn Yes/No => PC sẽ shutdown sau 5 phút
                }

                // Hết giờ
                if (time < 0)
                {
                    lblRemain.Text = "Timed out !";
                    timer.Stop();

                    switch (type)
                    {
                        case "Close":
                            form.Close();
                            break;
                        case "Sleep":
                            Application.SetSuspendState(PowerState.Suspend, true, true);
                            break;
                        case "Shutdown":
                            ProcessStartInfo shutdown = new ProcessStartInfo("shutdown", "-s -f -t 0");
                            shutdown.CreateNoWindow = true;
                            shutdown.UseShellExecute = false;
                            Process.Start(shutdown);
                            break;
                        default:
                            break;
                    }
                }
            };


            // Nút Player
            PictureBox pbPlayer = new PictureBox();
            pPower.Controls.Add(pbPlayer);
            pbPlayer.Location = new Point(476, 205);
            pbPlayer.Size = new Size(85, 85);
            pbPlayer.SizeMode = PictureBoxSizeMode.CenterImage;
            pbPlayer.BackColor = Color.Transparent;
            pbPlayer.Image = Resources.Player;

            // Zoom-in
            pbPlayer.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Player, pbPlayer, "Player");
            };

            // Zoom-out
            pbPlayer.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Player, pbPlayer);
            };

            // Click nút Player
            pbPlayer.Click += (sender, args) =>
            {
                wmp.BringToFront();
            };


            // Nút Home
            PictureBox pbHome = new PictureBox();
            pPower.Controls.Add(pbHome);
            pbHome.Location = new Point(469, 303);
            pbHome.Size = new Size(85, 85);
            pbHome.SizeMode = PictureBoxSizeMode.CenterImage;
            pbHome.BackColor = Color.Transparent;
            pbHome.Image = Resources.Home;

            // Zoom-in
            pbHome.MouseHover += (sender, args) =>
            {
                mouseHover(Resources.Home, pbHome, "Home");
            };

            // Zoom-out
            pbHome.MouseLeave += (sender, args) =>
            {
                mouseLeave(Resources.Home, pbHome);
            };

            // Click nút Home
            pbHome.Click += (sender, args) =>
            {
                pPower.SendToBack();
            };
        }
    }
}
