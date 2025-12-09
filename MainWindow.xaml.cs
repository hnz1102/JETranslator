using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Net.Http;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace JapaneseEnglishTranslator;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly HttpClient _httpClient;
    private readonly List<TranslationHistory> _translationHistory;
    private string _selectedModel = "gpt-4o"; // デフォルトモデル

    public MainWindow()
    {
        InitializeComponent();
        
        // OpenAI APIキーを環境変数から取得
        string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "YOUR_OPENAI_API_KEY";
        if (apiKey == "YOUR_OPENAI_API_KEY")
        {
            MessageBox.Show("OpenAI APIキーが設定されていません。\n環境変数 'OPENAI_API_KEY' にAPIキーを設定してください。", 
                          "設定エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        _translationHistory = new List<TranslationHistory>();
        
        // デフォルトでGPT-4oを選択
        ModelComboBox.SelectedIndex = 0;
    }

    private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModelComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
        {
            _selectedModel = selectedItem.Tag.ToString() ?? "gpt-4o";
            
            // モデル情報を更新
            switch (_selectedModel)
            {
                case "gpt-4o":
                    ModelInfoText.Text = "高精度・多機能モデル";
                    break;
                case "gpt-4o-mini":
                    ModelInfoText.Text = "高速・コスト効率モデル";
                    break;
                case "gpt-4-turbo":
                    ModelInfoText.Text = "バランス型高性能モデル";
                    break;
                case "gpt-3.5-turbo":
                    ModelInfoText.Text = "軽量・高速モデル";
                    break;
                default:
                    ModelInfoText.Text = "AI翻訳モデル";
                    break;
            }
            
            StatusText.Text = $"✅ モデル変更: {selectedItem.Content}";
        }
    }

    private async void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            await TranslateText();
        }
    }

    private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string inputText = InputTextBox.Text?.Trim() ?? string.Empty;
        bool isJapanese = ContainsJapanese(inputText);
        
        // 英語の場合のみ文法チェックボタンを有効化
        GrammarCheckButton.IsEnabled = !string.IsNullOrEmpty(inputText) && !isJapanese;
        
        // ボタンの色も変更
        if (GrammarCheckButton.IsEnabled)
        {
            GrammarCheckButton.Background = new SolidColorBrush(Color.FromRgb(255, 107, 53)); // オレンジ
        }
        else
        {
            GrammarCheckButton.Background = new SolidColorBrush(Color.FromRgb(149, 165, 166)); // グレー
        }
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        await TranslateText();
    }

    private async void GrammarCheckButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckGrammar();
    }

    private async Task TranslateText()
    {
        string inputText = InputTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(inputText))
            return;

        try
        {
            StatusText.Text = "翻訳中...";
            TranslateButton.IsEnabled = false;

            // 入力言語を判定
            bool isJapanese = ContainsJapanese(inputText);
            
            string systemMessage;
            string targetLanguage;
            if (isJapanese)
            {
                systemMessage = "あなたは日本語から英語への翻訳者です。入力された日本語を自然で正確な英語に翻訳してください。翻訳結果のみを返してください。";
                targetLanguage = "English";
            }
            else
            {
                systemMessage = "あなたは英語から日本語への翻訳者です。入力された英語を自然で正確な日本語に翻訳してください。翻訳結果のみを返してください。";
                targetLanguage = "日本語";
            }

            // OpenAI APIを使用して翻訳
            var requestBody = new
            {
                model = _selectedModel,
                messages = new[]
                {
                    new { role = "system", content = systemMessage },
                    new { role = "user", content = inputText }
                }
            };

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", 
                new StringContent(JsonConvert.SerializeObject(requestBody), System.Text.Encoding.UTF8, "application/json"));

            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            dynamic? json = JsonConvert.DeserializeObject(responseBody);
            string translatedText = json?.choices?[0]?.message?.content?.ToString() ?? "翻訳に失敗しました";

            // 履歴に追加
            var historyItem = new TranslationHistory
            {
                OriginalText = inputText,
                TranslatedText = translatedText,
                SourceLanguage = isJapanese ? "日本語" : "English",
                TargetLanguage = targetLanguage,
                Timestamp = DateTime.Now
            };
            _translationHistory.Add(historyItem);

            // UIに履歴項目を追加
            AddHistoryItem(historyItem);

            // 入力フィールドをクリア
            InputTextBox.Clear();
            StatusText.Text = "翻訳完了";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"翻訳エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "翻訳エラー";
        }
        finally
        {
            TranslateButton.IsEnabled = true;
        }
    }

    private async Task CheckGrammar()
    {
        string inputText = InputTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(inputText) || ContainsJapanese(inputText))
            return;

        try
        {
            StatusText.Text = "文法チェック中...";
            GrammarCheckButton.IsEnabled = false;

            // OpenAI APIを使用して文法チェック
            string systemMessage = @"あなたは英語の文法チェッカーです。入力された英語文を分析し、以下の形式で回答してください：

【文法チェック結果】
✅ 文法的に正しい場合: 「正しい英語です」
❌ 誤りがある場合: 誤りの内容を指摘

【修正提案】
修正版の文章（誤りがある場合のみ）

【説明】
誤りの理由や改善点の説明

【正しい英文】
最終的に正しい英文（修正がある場合は修正版、正しい場合は元の文章）

簡潔で分かりやすく回答してください。";

            var requestBody = new
            {
                model = _selectedModel, // 選択されたモデルを使用
                messages = new[]
                {
                    new { role = "system", content = systemMessage },
                    new { role = "user", content = inputText }
                }
            };

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", 
                new StringContent(JsonConvert.SerializeObject(requestBody), System.Text.Encoding.UTF8, "application/json"));

            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            dynamic? json = JsonConvert.DeserializeObject(responseBody);
            string grammarCheckResult = json?.choices?[0]?.message?.content?.ToString() ?? "文法チェックに失敗しました";

            // 正しい英文を抽出
            string correctedText = ExtractCorrectedText(grammarCheckResult, inputText);

            // 文法チェック結果を履歴に追加
            var grammarHistoryItem = new GrammarCheckHistory
            {
                OriginalText = inputText,
                CheckResult = grammarCheckResult,
                CorrectedText = correctedText,
                Timestamp = DateTime.Now
            };

            // UIに文法チェック結果を追加
            AddGrammarCheckItem(grammarHistoryItem);

            // 入力フィールドをクリア
            InputTextBox.Clear();
            StatusText.Text = "文法チェック完了";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"文法チェックエラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "文法チェックエラー";
        }
        finally
        {
            GrammarCheckButton.IsEnabled = true;
        }
    }

    private string ExtractCorrectedText(string grammarResult, string originalText)
    {
        // 【正しい英文】セクションから英文を抽出
        var match = Regex.Match(grammarResult, @"【正しい英文】\s*\n(.+?)(?:\n\n|\n【|$)", RegexOptions.Singleline);
        if (match.Success && match.Groups.Count > 1)
        {
            return match.Groups[1].Value.Trim();
        }

        // 【修正提案】セクションから英文を抽出（代替手段）
        match = Regex.Match(grammarResult, @"【修正提案】\s*\n(.+?)(?:\n\n|\n【|$)", RegexOptions.Singleline);
        if (match.Success && match.Groups.Count > 1)
        {
            return match.Groups[1].Value.Trim();
        }

        // どちらも見つからない場合は元のテキストを返す
        return originalText;
    }

    private bool ContainsJapanese(string text)
    {
        // ひらがな、カタカナ、漢字の範囲をチェック
        foreach (char c in text)
        {
            // ひらがな (U+3040-U+309F)
            if (c >= 0x3040 && c <= 0x309F) return true;
            // カタカナ (U+30A0-U+30FF)
            if (c >= 0x30A0 && c <= 0x30FF) return true;
            // 漢字 (U+4E00-U+9FAF)
            if (c >= 0x4E00 && c <= 0x9FAF) return true;
            // 全角英数字・記号 (U+FF00-U+FFEF)
            if (c >= 0xFF00 && c <= 0xFFEF) return true;
        }
        return false;
    }

    private void AddHistoryItem(TranslationHistory item)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(225, 232, 237)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 10, 0, 10),
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush(
                Color.FromRgb(248, 249, 250), 
                Color.FromRgb(255, 255, 255), 
                90)
        };

        // ドロップシャドウ効果を追加
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(0, 0, 0),
            BlurRadius = 5,
            ShadowDepth = 2,
            Opacity = 0.1
        };

        var stackPanel = new StackPanel();

        // タイムスタンプ
        var timestampText = new TextBlock
        {
            Text = $"🕒 {item.Timestamp.ToString("yyyy/MM/dd HH:mm:ss")}",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(149, 165, 166)),
            Margin = new Thickness(0, 0, 0, 12),
            FontWeight = FontWeights.Normal
        };

        // 原文表示 - 縦方向のスタックパネルに変更
        var originalPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 15)
        };
        
        var sourceFlag = item.SourceLanguage == "日本語" ? "🇯🇵" : "🇺🇸";
        var originalLabel = new TextBlock
        {
            Text = $"{sourceFlag} {item.SourceLanguage}:",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(52, 73, 94)),
            Margin = new Thickness(0, 0, 0, 5)
        };

        var originalText = new TextBlock
        {
            Text = item.OriginalText,
            FontSize = 15,
            FontWeight = FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(44, 62, 80)),
            LineHeight = 22,
            MaxWidth = 820, // 最大幅を設定して確実に折り返し
            Margin = new Thickness(10, 0, 0, 0)
        };

        originalPanel.Children.Add(originalLabel);
        originalPanel.Children.Add(originalText);

        // 翻訳結果表示 - 縦方向のスタックパネルに変更
        var translatedPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 15)
        };

        var targetFlag = item.TargetLanguage == "日本語" ? "🇯🇵" : "🇺🇸";
        var translatedLabel = new TextBlock
        {
            Text = $"{targetFlag} {item.TargetLanguage}:",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(52, 152, 219)),
            Margin = new Thickness(0, 0, 0, 5)
        };

        var translatedText = new TextBlock
        {
            Text = item.TranslatedText,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(41, 128, 185)),
            LineHeight = 22,
            MaxWidth = 820, // 最大幅を設定して確実に折り返し
            Margin = new Thickness(10, 0, 0, 0)
        };

        translatedPanel.Children.Add(translatedLabel);
        translatedPanel.Children.Add(translatedText);

        // スタイリッシュなコピーボタン
        var copyButton = new Button
        {
            Content = "📋 コピー",
            Width = 120,
            Height = 35,
            HorizontalAlignment = HorizontalAlignment.Left,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };

        // コピーボタンにスタイルを適用
        copyButton.Style = (Style)this.FindResource("CopyButton");
        
        // ボタンにドロップシャドウ効果を追加
        copyButton.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(39, 174, 96),
            BlurRadius = 6,
            ShadowDepth = 2,
            Opacity = 0.25
        };

        copyButton.Click += (s, e) => CopyToClipboard(item.TranslatedText);

        stackPanel.Children.Add(timestampText);
        stackPanel.Children.Add(originalPanel);
        stackPanel.Children.Add(translatedPanel);
        stackPanel.Children.Add(copyButton);

        border.Child = stackPanel;
        HistoryPanel.Children.Insert(0, border); // 最新の項目を上部に追加

        // 自動スクロールを最上部に
        if (HistoryPanel.Children.Count > 0)
        {
            var scrollViewer = FindParent<ScrollViewer>(HistoryPanel);
            scrollViewer?.ScrollToTop();
        }
    }

    private void AddGrammarCheckItem(GrammarCheckHistory item)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 107, 53)),
            BorderThickness = new Thickness(2),
            Margin = new Thickness(0, 10, 0, 10),
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush(
                Color.FromRgb(255, 248, 240), 
                Color.FromRgb(255, 255, 255), 
                90)
        };

        // ドロップシャドウ効果を追加
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(255, 107, 53),
            BlurRadius = 5,
            ShadowDepth = 2,
            Opacity = 0.2
        };

        var stackPanel = new StackPanel();

        // タイムスタンプ
        var timestampText = new TextBlock
        {
            Text = $"🕒 {item.Timestamp.ToString("yyyy/MM/dd HH:mm:ss")}",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(149, 165, 166)),
            Margin = new Thickness(0, 0, 0, 12),
            FontWeight = FontWeights.Normal
        };

        // 文法チェック対象文表示
        var originalPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 15)
        };
        
        var originalLabel = new TextBlock
        {
            Text = "📝 チェック対象文:",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 53)),
            Margin = new Thickness(0, 0, 0, 5)
        };

        var originalText = new TextBlock
        {
            Text = item.OriginalText,
            FontSize = 15,
            FontWeight = FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(44, 62, 80)),
            LineHeight = 22,
            MaxWidth = 820,
            Margin = new Thickness(10, 0, 0, 0)
        };

        originalPanel.Children.Add(originalLabel);
        originalPanel.Children.Add(originalText);

        // 文法チェック結果表示
        var resultPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 15)
        };

        var resultLabel = new TextBlock
        {
            Text = "🔍 文法チェック結果:",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 53)),
            Margin = new Thickness(0, 0, 0, 5)
        };

        var resultText = new TextBlock
        {
            Text = item.CheckResult,
            FontSize = 15,
            FontWeight = FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(44, 62, 80)),
            LineHeight = 22,
            MaxWidth = 820,
            Margin = new Thickness(10, 0, 0, 0)
        };

        resultPanel.Children.Add(resultLabel);
        resultPanel.Children.Add(resultText);

        // 修正された正しい英文表示
        var correctedPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 15)
        };

        var correctedLabel = new TextBlock
        {
            Text = "✅ 正しい英文:",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96)),
            Margin = new Thickness(0, 0, 0, 5)
        };

        var correctedText = new TextBlock
        {
            Text = item.CorrectedText,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96)),
            LineHeight = 22,
            MaxWidth = 820,
            Margin = new Thickness(10, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(240, 255, 240)),
            Padding = new Thickness(10, 8, 10, 8)
        };

        correctedPanel.Children.Add(correctedLabel);
        correctedPanel.Children.Add(correctedText);

        // ボタンパネル（横並び）
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0)
        };

        // 結果をコピーボタン
        var copyResultButton = new Button
        {
            Content = "📋 結果をコピー",
            Width = 140,
            Height = 35,
            Margin = new Thickness(0, 0, 10, 0),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromRgb(255, 107, 53)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };

        copyResultButton.Style = (Style)this.FindResource("CopyButton");
        copyResultButton.Background = new SolidColorBrush(Color.FromRgb(255, 107, 53));
        
        copyResultButton.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(255, 107, 53),
            BlurRadius = 6,
            ShadowDepth = 2,
            Opacity = 0.25
        };

        copyResultButton.Click += (s, e) => CopyToClipboard(item.CheckResult);

        // 正しい英文をコピーボタン
        var copyCorrectedButton = new Button
        {
            Content = "📝 正しい英文をコピー",
            Width = 160,
            Height = 35,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromRgb(39, 174, 96)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };

        copyCorrectedButton.Style = (Style)this.FindResource("CopyButton");
        copyCorrectedButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96));
        
        copyCorrectedButton.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(39, 174, 96),
            BlurRadius = 6,
            ShadowDepth = 2,
            Opacity = 0.25
        };

        copyCorrectedButton.Click += (s, e) => CopyToClipboard(item.CorrectedText);

        buttonPanel.Children.Add(copyResultButton);
        buttonPanel.Children.Add(copyCorrectedButton);

        stackPanel.Children.Add(timestampText);
        stackPanel.Children.Add(originalPanel);
        stackPanel.Children.Add(resultPanel);
        stackPanel.Children.Add(correctedPanel);
        stackPanel.Children.Add(buttonPanel);

        border.Child = stackPanel;
        HistoryPanel.Children.Insert(0, border);

        // 自動スクロールを最上部に
        if (HistoryPanel.Children.Count > 0)
        {
            var scrollViewer = FindParent<ScrollViewer>(HistoryPanel);
            scrollViewer?.ScrollToTop();
        }
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            StatusText.Text = "クリップボードにコピーしました";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"コピーエラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject == null) return null;
        T? parent = parentObject as T;
        return parent ?? FindParent<T>(parentObject);
    }

    // カスタムウィンドウコントロール用のイベントハンドラー
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            this.DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // 確認ダイアログを表示
        MessageBoxResult result = MessageBox.Show(
            "アプリケーションを終了しますか？", 
            "終了確認", 
            MessageBoxButton.YesNo, 
            MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            Application.Current.Shutdown();
        }
    }
}

public class TranslationHistory
{
    public required string OriginalText { get; set; }
    public required string TranslatedText { get; set; }
    public required string SourceLanguage { get; set; }
    public required string TargetLanguage { get; set; }
    public DateTime Timestamp { get; set; }
}

public class GrammarCheckHistory
{
    public required string OriginalText { get; set; }
    public required string CheckResult { get; set; }
    public required string CorrectedText { get; set; } // 修正されたテキスト
    public DateTime Timestamp { get; set; }
}