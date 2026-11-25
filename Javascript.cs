using System;
using System.IO;
using System.Net;
using System.Text;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

sealed partial class Editor
{
    string wrapperFile;
    void ClearJavascript(){
        if(wrapperFile==null)return;
        try {File.Delete(wrapperFile);}catch(IOException){}catch(UnauthorizedAccessException){}
        wrapperFile=null;
    }
    void OpenJavascript(WebView2 view,string target)
    {
        var core=view.CoreWebView2;
        string dir=root!=null&&InsideRoot(target)?root:Path.GetDirectoryName(target);
        string relative=target.Substring(dir.TrimEnd('\\').Length+1).Replace('\\','/');
        string uri="https://studio-preview.invalid/"+String.Join("/",Array.ConvertAll(relative.Split('/'),s=>Uri.EscapeDataString(s)));
        string wrapper=uri.Substring(0,uri.LastIndexOf('/')+1)+"studio-"+Guid.NewGuid().ToString("N")+".html";
        core.SetVirtualHostNameToFolderMapping("studio-preview.invalid",dir,CoreWebView2HostResourceAccessKind.DenyCors);
        string html="<!doctype html><meta charset=utf-8><title>JavaScript</title><style>body{font:16px system-ui;color:#24344c}pre{white-space:pre-wrap;overflow-wrap:anywhere}</style><main id='app'></main><pre id='output'></pre><script>"+
            "const report=(...args)=>{const p=document.getElementById('output');if(p)p.append(document.createTextNode(args.map(v=>{try{return typeof v==='string'?v:JSON.stringify(v)??String(v)}catch{return String(v)}}).join(' ')+'\\n'))};"+
            "for(const k of ['log','info','warn','error','debug']){const old=console[k].bind(console);console[k]=(...a)=>{old(...a);report(...a)}};"+
            "addEventListener('error',e=>report(e.message||'読み込みエラー'));addEventListener('unhandledrejection',e=>report(String(e.reason)));</script>"+
            "<script type='module' src='"+WebUtility.HtmlEncode(uri)+"'></script>";
        string tmp=Path.Combine(Path.GetDirectoryName(target),new Uri(wrapper).Segments[new Uri(wrapper).Segments.Length-1]);
        using(var stream=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){
            wrapperFile=tmp;byte[] bytes=Encoding.UTF8.GetBytes(html);stream.Write(bytes,0,bytes.Length);
        }
        core.NavigationCompleted+=(s,e)=>{if(wrapperFile==tmp)ClearJavascript();};
        core.Navigate(wrapper);
    }
}
