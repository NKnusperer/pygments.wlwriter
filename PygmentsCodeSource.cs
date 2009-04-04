﻿using System;
using System.Windows.Forms;
using WindowsLive.Writer.Api;
using Microsoft.Scripting.Hosting;
using IronPython.Runtime;
using System.IO;
using System.Threading;
using System.Collections.Generic;

namespace DevHawk
{
    public class WaitCursor : IDisposable
    {
        private Cursor _original;

        public WaitCursor() : this(Cursors.WaitCursor)
        {
        }

        public WaitCursor(Cursor cursor)
        {
            _original = Cursor.Current;
            Cursor.Current = cursor;
        }

        public void Dispose()
        {
            Cursor.Current = _original;
        }
    }

    public class PygmentLanguage
    {
        public string LongName;
        public string LookupName;

        public override string ToString()
        {
            return LongName;
        }
    }

    [WriterPlugin("2EC9848E-067D-4e79-BAB7-06CA927DB962", "Pygments.WLWriter",
        Description = "Code Colorizer using Python pygments package", 
        ImagePath="icon_16.png",
        PublisherUrl = "http://devhawk.net")]
    [InsertableContentSource("Insert Pygmented Code", SidebarText = "Pygmented Code")]
    public class PygmentsCodeSource : SmartContentSource
    {
        static ScriptEngine _engine;
        static ScriptSource _source;

        ScriptScope _scope;
        Thread _init_thread;

        private void InitializeHosting()
        {
            var asm = System.Reflection.Assembly.GetAssembly(typeof(PygmentsCodeSource));
            var folder = Path.GetDirectoryName(asm.Location);
            
            _engine = IronPython.Hosting.Python.CreateEngine();
            _engine.SetSearchPaths(new string[] { folder, @"c:\Program Files\IronPython 2.0.1\Lib" });

            _source = _engine.CreateScriptSourceFromFile(Path.Combine(folder, "PygmentsCodeSource.py"));
        }

        public PygmentsCodeSource()
        {
            if (_engine == null)
                InitializeHosting();

            try
            {
                 _scope = _engine.CreateScope();

                _init_thread = new Thread(() =>
                    {
                        _source.Execute(_scope);
                    });

                _init_thread.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, ex.Source);
            }
        }

        string[] _styles;
        PygmentLanguage[] _lanugages;

        public PygmentLanguage[] Languages
        {
            get
            {
                if (_lanugages == null)
                {
                    using (var wc = new WaitCursor())
                    {
                        _init_thread.Join();

                        var f = _scope.GetVariable<PythonFunction>("get_lexers");
                        var r = (PythonGenerator)f.Target.DynamicInvoke();
                        var lanugages_list = new List<PygmentLanguage>();
                        foreach (PythonTuple o in r)
                        {
                            lanugages_list.Add(new PygmentLanguage()
                                {
                                    LongName = (string)o[0],
                                    LookupName = (string)((PythonTuple)o[1])[0]
                                });
                        }

                        _lanugages = lanugages_list.ToArray();
                    }
                }

                return _lanugages;
            }
        }

        public string[] Styles
        {
            get
            {
                if (_styles == null)
                {
                    using (var wc = new WaitCursor())
                    {
                        _init_thread.Join();

                        var f = _scope.GetVariable<PythonFunction>("get_styles");
                        var r = (PythonGenerator)f.Target.DynamicInvoke();
                        var styles_list = new List<string>();
                        foreach (string o in r)
                        {
                            styles_list.Add(o);
                        }

                        _styles = styles_list.ToArray();
                    }
                }

                return _styles;
            }
        }

        PythonFunction _highlight_function;

        public string Highlight(string code, string lexer_name, string style_name)
        {
            if (_highlight_function == null)
            {
                using (var wc = new WaitCursor())
                {
                    _init_thread.Join();

                    _highlight_function = _scope.GetVariable<PythonFunction>("generate_html");
                }
            }

            using (var wc = new WaitCursor())
            {
                return (string)_highlight_function.Target.DynamicInvoke(code, lexer_name, style_name);
            }
        }

        public override DialogResult CreateContent(IWin32Window dialogOwner, ISmartContent newContent)
        {
            var form = new CodeInsertForm();
            var result = form.ShowDialog(dialogOwner);
            if (result == DialogResult.OK) 
            {
                newContent.Properties["code"] = form.Code;
            }

            return result;
        }


        

        public override SmartContentEditor CreateEditor(ISmartContentEditorSite editorSite)
        {
            return new PygmentsCodeSidebar(Languages, Styles);
        }

        public override string GeneratePublishHtml(ISmartContent content, IPublishingContext publishingContext)
        {
            if (!content.Properties.Contains("html"))
            {
                content.Properties["html"] = Highlight(
                    content.Properties["code"],
                    content.Properties["language"],
                    content.Properties["style"]);
            }
            return content.Properties["html"];
        }
    }
}
