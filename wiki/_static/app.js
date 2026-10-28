(function () {
  var content = document.getElementById("content");
  var nav = document.getElementById("sidebar-nav");
  var brandSub = document.querySelector(".brand .sub");

  function docs() {
    return window.SSF2_DOCS || null;
  }

  function route() {
    var raw = (location.hash || "#/").replace(/^#\/?/, "");
    if (!raw) return showHome();
    var anchor = "";
    var cut = raw.indexOf("#");
    if (cut >= 0) {
      anchor = decodeURIComponent(raw.slice(cut + 1));
      raw = raw.slice(0, cut);
    }
    return showPage(decodeURIComponent(raw), anchor);
  }

  function showError(msg) {
    content.innerHTML =
      "<h1>Docs error</h1><div class='warning'><p>" +
      msg +
      "</p><p>From <code>shell-dev\\docs</code> double-click <code>Open Docs.bat</code> to rebuild.</p></div>";
  }

  function loadBundle() {
    var data = docs();
    if (!data || !data.sections) {
      throw new Error("content.js missing or empty — run Open Docs.bat");
    }
    return data;
  }

  function buildNav(manifest, currentFile) {
    var html = "";
    html += '<a href="#/"' + (!currentFile ? ' class="current"' : "") + ">Home</a>";
    (manifest.sections || []).forEach(function (sec) {
      html += '<div class="section">' + escape(sec.title) + "</div>";
      (sec.pages || []).forEach(function (p) {
        var cur = p.file === currentFile ? ' class="current"' : "";
        html +=
          '<a href="#/' +
          encodeURIComponent(p.file) +
          '"' +
          cur +
          ">" +
          escape(p.title) +
          "</a>";
      });
    });
    nav.innerHTML = html;
  }

  function escape(s) {
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function showHome() {
    try {
      var manifest = loadBundle();
      buildNav(manifest, null);
      if (brandSub) brandSub.textContent = "Wiki";
      document.title = "SSF2 Mod Manager Wiki";
      var parts = [];
      parts.push("<h1>SSF2 Mod Manager Wiki</h1>");
      parts.push(
        '<p class="lead">New to SSF2 modding? Start with the launcher. Download the latest release, run SSF2ModManager.exe, then use Play / Debug / Make Mods from there.</p>'
      );
      parts.push(
        '<div class="tip"><strong>Get the build</strong> from ' +
          '<a href="https://github.com/SSF2-Mods-Official/SSF2ModManager/releases/latest">GitHub Releases (latest)</a>. ' +
          "Quick Launch and Debug open SSF2 Stream. Use <strong>Open Docs</strong> on the launcher to open this wiki.</div>"
      );

      (manifest.sections || []).forEach(function (sec) {
        parts.push("<h2>" + escape(sec.title) + "</h2>");
        parts.push('<div class="cards">');
        (sec.pages || []).forEach(function (p) {
          parts.push(
            '<a class="card" href="#/' +
              encodeURIComponent(p.file) +
              '"><strong>' +
              escape(p.title) +
              "</strong><span>" +
              escape(p.summary || "") +
              "</span></a>"
          );
        });
        parts.push("</div>");
      });

      if (!(manifest.sections || []).length) {
        parts.push(
          '<div class="warning"><p>No doc sections found. Add folders like <code>Start Here</code> with <code>.md</code> files, then run <code>Open Docs.bat</code>.</p></div>'
        );
      }

      content.innerHTML = parts.join("\n");
    } catch (err) {
      showError(escape(err.message || String(err)));
    }
  }

  function showPage(file, anchor) {
    try {
      var manifest = loadBundle();
      var raw = (manifest.pages && manifest.pages[file]) || null;
      if (raw == null) {
        throw new Error("Unknown page: " + file + " — run Open Docs.bat after adding .md files");
      }
      buildNav(manifest, file);
      renderArticle(file, raw);
      var target = anchor ? document.getElementById(anchor) : null;
      if (target) target.scrollIntoView();
      else window.scrollTo(0, 0);
    } catch (err) {
      showError(escape(err.message || String(err)));
    }
  }

  function renderArticle(file, raw) {
    var parsed = Ssf2DocsMd.parseFrontMatter(raw);
    var title = parsed.meta.title || file;
    if (brandSub) brandSub.textContent = title;
    document.title = title + " — SSF2 Mod Manager Wiki";
    var bodyHtml = Ssf2DocsMd.renderMarkdown(parsed.body);
    content.innerHTML =
      '<article class="article"><h1>' +
      escape(title) +
      "</h1>" +
      (parsed.meta.summary ? '<p class="lead">' + escape(parsed.meta.summary) + "</p>" : "") +
      bodyHtml +
      "</article>";
    var h1s = content.querySelectorAll("article h1");
    if (h1s.length > 1) h1s[1].remove();
    fixLinks(file);
      if (window.wiki) mountPageFoot(file, raw);
      if (window.wiki && sessionStorage.getItem("wiki-open-edit") === file) {
        sessionStorage.removeItem("wiki-open-edit");
        openEditor(file, raw);
      }
  }

  function fixLinks(file) {
    var baseDir = file.replace(/[^/]+$/, "");
    content.querySelectorAll("img[data-rel]").forEach(function (img) {
      img.setAttribute("src", "../" + resolveRel(baseDir, img.getAttribute("data-rel") || ""));
    });
    content.querySelectorAll('a[href^="#md-rel:"]').forEach(function (a) {
      var rel = decodeURIComponent(a.getAttribute("href").slice(8));
      a.setAttribute("href", "#/" + encodeURIComponent(resolveRel(baseDir, rel)));
    });
    content.querySelectorAll("a[href]").forEach(function (a) {
      var href = a.getAttribute("href") || "";
      if (href.charAt(0) === "#" && href.indexOf("#/") !== 0 && href.indexOf("#md-rel:") !== 0) {
        a.setAttribute("href", "#/" + encodeURIComponent(file) + href);
      }
    });
  }

  function resolveRel(baseDir, rel) {
    var parts = (baseDir + rel).split("/");
    var out = [];
    parts.forEach(function (p) {
      if (p === "" || p === ".") return;
      if (p === "..") out.pop();
      else out.push(p);
    });
    return out.join("/");
  }

  function versionRows(record) {
    var entries = (record && record.entries) || [];
    var userMade = !!(record && record.userMade);
    var rows = entries.map(function (row, i) {
      return {
        version: entries.length - i + (userMade ? 0 : 1),
        author: row.author,
        note: row.note,
        at: row.at
      };
    });
    if (!userMade) {
      rows.push({ version: 1, author: "Mod Manager", note: "Published", at: "" });
    }
    return rows;
  }

  function historyHtml(rows) {
    if (!rows.length) return "<h2>Version history</h2><p>Not published yet.</p>";
    return (
      "<h2>Version history</h2><ul>" +
      rows
        .map(function (row) {
          return (
            "<li><strong>Version " +
            row.version +
            "</strong> " +
            escape(row.author || "") +
            " — " +
            escape(row.note || "") +
            (row.at ? " <span>" + escape(row.at) + "</span>" : "") +
            "</li>"
          );
        })
        .join("") +
      "</ul>"
    );
  }

  function mountPageFoot(file, raw) {
    var foot = document.createElement("div");
    foot.className = "page-foot";
    foot.innerHTML = '<section class="history"><p>Loading history…</p></section>';
    content.appendChild(foot);
    window.wiki.history(file).then(function (record) {
      var rows = versionRows(record);
      var html = historyHtml(rows);
      html += '<div class="page-actions"><button type="button" id="edit-page">Edit</button>';
      if (record && record.userMade) {
        html += '<button type="button" id="pkg-page">Package tutorial</button>';
        html += '<button type="button" class="danger" id="del-page">Delete</button>';
      }
      html += "</div>";
      foot.innerHTML = html;
      foot.querySelector("#edit-page").addEventListener("click", function () {
        openEditor(file, raw);
      });
      var pkg = foot.querySelector("#pkg-page");
      if (pkg) {
        pkg.addEventListener("click", function () {
          window.wiki.author().then(function (author) {
            return window.wiki.packagePage(file, author || "");
          }).catch(function (err) {
            showError(escape(err && err.message ? err.message : String(err)));
          });
        });
      }
      var del = foot.querySelector("#del-page");
      if (del) {
        del.addEventListener("click", function () {
          askConfirm("Delete this page?", "Delete").then(function (ok) {
            if (!ok) return;
            window.wiki.deletePage(file).then(function () {
              location.hash = "#/";
              location.reload();
            }).catch(function (err) {
              showError(escape(err && err.message ? err.message : String(err)));
            });
          });
        });
      }
    });
  }

  function openEditor(file, raw) {
    content.innerHTML = "";
    var editor = document.createElement("div");
    editor.className = "md-editor";
    editor.innerHTML =
      '<div class="md-toolbar">' +
      '<div class="md-modes">' +
      '<button type="button" data-mode="write" class="on">Write</button>' +
      '<button type="button" data-mode="preview">Preview</button>' +
      "</div>" +
      '<div class="md-tools">' +
      '<button type="button" data-act="h1" title="Heading 1">H1</button>' +
      '<button type="button" data-act="h2" title="Heading 2">H2</button>' +
      '<button type="button" data-act="h3" title="Heading 3">H3</button>' +
      '<button type="button" data-act="bold" title="Bold"><b>B</b></button>' +
      '<button type="button" data-act="italic" title="Italic"><i>I</i></button>' +
      '<button type="button" data-act="strike" title="Strikethrough"><s>S</s></button>' +
      '<button type="button" data-act="quote" title="Quote">“”</button>' +
      '<button type="button" data-act="code" title="Inline code">&lt;/&gt;</button>' +
      '<button type="button" data-act="fence" title="Code block">{ }</button>' +
      '<button type="button" data-act="link" title="Link">Link</button>' +
      '<button type="button" data-act="image" title="Image">Image</button>' +
      '<select id="edit-images" title="Insert an image"><option value="">Images</option></select>' +
      '<button type="button" data-act="ul" title="Bullet list">• List</button>' +
      '<button type="button" data-act="ol" title="Numbered list">1. List</button>' +
      '<button type="button" data-act="hr" title="Horizontal rule">―</button>' +
      '<button type="button" data-act="table" title="Table">Table</button>' +
      "</div>" +
      "</div>" +
      '<textarea id="edit-body" spellcheck="false"></textarea>' +
      '<div class="md-preview article" hidden></div>' +
      '<div class="md-footer">' +
      '<button type="button" id="edit-cancel">Cancel</button>' +
      '<button type="button" id="edit-save">Save</button>' +
      "</div>";
    content.appendChild(editor);
    var box = editor.querySelector("#edit-body");
    box.value = raw;
    window.wiki.images().then(function (names) {
      var sel = editor.querySelector("#edit-images");
      (names || []).forEach(function (name) {
        var opt = document.createElement("option");
        opt.value = name;
        opt.textContent = name;
        sel.appendChild(opt);
      });
    });

    function setMode(mode) {
      var write = mode !== "preview";
      box.hidden = !write;
      var preview = editor.querySelector(".md-preview");
      preview.hidden = write;
      editor.querySelectorAll(".md-modes button").forEach(function (b) {
        b.classList.toggle("on", b.getAttribute("data-mode") === mode);
      });
      if (!write) {
        var parsed = Ssf2DocsMd.parseFrontMatter(box.value);
        var title = parsed.meta.title || file;
        preview.innerHTML =
          "<h1>" +
          escape(title) +
          "</h1>" +
          (parsed.meta.summary ? '<p class="lead">' + escape(parsed.meta.summary) + "</p>" : "") +
          Ssf2DocsMd.renderMarkdown(parsed.body);
        var h1s = preview.querySelectorAll("h1");
        if (h1s.length > 1) h1s[1].remove();
        preview.querySelectorAll("img[data-rel]").forEach(function (img) {
          var baseDir = file.replace(/[^/]+$/, "");
          img.setAttribute("src", "../" + resolveRel(baseDir, img.getAttribute("data-rel") || ""));
        });
      }
    }

    editor.querySelector(".md-modes").addEventListener("click", function (ev) {
      var mode = ev.target.getAttribute && ev.target.getAttribute("data-mode");
      if (mode) setMode(mode);
    });
    editor.querySelector(".md-tools").addEventListener("click", function (ev) {
      var act = ev.target.getAttribute && ev.target.getAttribute("data-act");
      if (!act && ev.target.parentElement) act = ev.target.parentElement.getAttribute("data-act");
      if (!act) return;
      applyAction(act);
    });
    editor.querySelector("#edit-images").addEventListener("change", function (ev) {
      if (!ev.target.value) return;
      insertImage(ev.target.value);
      ev.target.value = "";
    });
    editor.querySelector("#edit-cancel").addEventListener("click", function () {
      renderArticle(file, raw);
    });
    editor.querySelector("#edit-save").addEventListener("click", function () {
      askSave().then(function (fields) {
        if (!fields) return;
        window.wiki
          .savePage(file, box.value, fields.author, fields.note)
          .then(function () {
            location.reload();
          })
          .catch(function (err) {
            showError(escape(err && err.message ? err.message : String(err)));
          });
      });
    });

    function applyAction(act) {
      if (act === "bold") return wrap("**", "**", "bold");
      if (act === "italic") return wrap("*", "*", "italic");
      if (act === "strike") return wrap("~~", "~~", "strike");
      if (act === "h1") return linePrefix("# ");
      if (act === "h2") return linePrefix("## ");
      if (act === "h3") return linePrefix("### ");
      if (act === "quote") return linePrefix("> ");
      if (act === "code") return wrap("`", "`", "code");
      if (act === "fence") return wrap("\n```\n", "\n```\n", "code");
      if (act === "link") return wrap("[", "](https://)", "text");
      if (act === "ul") return linePrefix("- ");
      if (act === "ol") return linePrefix("1. ");
      if (act === "hr") return insert("\n\n---\n\n");
      if (act === "table") {
        return insert("\n\n| Column | Column |\n| --- | --- |\n|  |  |\n\n");
      }
      if (act === "image") {
        window.wiki.pickImage().then(function (name) {
          if (!name) return;
          var sel = editor.querySelector("#edit-images");
          var opt = document.createElement("option");
          opt.value = name;
          opt.textContent = name;
          sel.appendChild(opt);
          insertImage(name);
        });
      }
    }

    function wrap(before, after, placeholder) {
      var start = box.selectionStart;
      var end = box.selectionEnd;
      var sel = box.value.slice(start, end) || placeholder;
      box.value = box.value.slice(0, start) + before + sel + after + box.value.slice(end);
      box.focus();
      box.selectionStart = start + before.length;
      box.selectionEnd = start + before.length + sel.length;
    }

    function linePrefix(prefix) {
      var start = box.selectionStart;
      var line = box.value.lastIndexOf("\n", start - 1) + 1;
      box.value = box.value.slice(0, line) + prefix + box.value.slice(line);
      box.focus();
      box.selectionStart = box.selectionEnd = start + prefix.length;
    }

    function insert(snippet) {
      var start = box.selectionStart;
      box.value = box.value.slice(0, start) + snippet + box.value.slice(start);
      box.focus();
      box.selectionStart = box.selectionEnd = start + snippet.length;
    }

    function insertImage(name) {
      var depth = file.split("/").length - 1;
      var rel = "../".repeat(Math.max(depth, 0)) + "_images/" + name;
      insert("\n![" + name + "](" + rel + ")\n");
    }
  }

  function askSave() {
    return window.wiki.author().then(function (saved) {
      return askForm(
        "Save page",
        [
          { id: "author", label: "Author", value: saved || "" },
          { id: "note", label: "What changed", value: "" }
        ],
        "Save"
      ).then(function (fields) {
        if (!fields) return null;
        if (!fields.author.trim() || !fields.note.trim()) {
          showError("Author and what changed are both required.");
          return null;
        }
        return { author: fields.author.trim(), note: fields.note.trim() };
      });
    });
  }

  function askConfirm(title, okLabel) {
    return new Promise(function (resolve) {
      var back = document.createElement("div");
      back.className = "modal-back";
      back.innerHTML =
        '<form class="modal"><h2>' +
        escape(title) +
        '</h2><div class="modal-actions"><button type="button" class="ghost">Cancel</button><button type="submit" class="danger">' +
        escape(okLabel) +
        "</button></div></form>";
      document.body.appendChild(back);
      back.querySelector(".ghost").addEventListener("click", function () {
        back.remove();
        resolve(false);
      });
      back.querySelector("form").addEventListener("submit", function (ev) {
        ev.preventDefault();
        back.remove();
        resolve(true);
      });
    });
  }

  function askForm(title, fields, okLabel) {
    return new Promise(function (resolve) {
      var back = document.createElement("div");
      back.className = "modal-back";
      var html = '<form class="modal"><h2>' + escape(title) + "</h2>";
      fields.forEach(function (f) {
        html +=
          "<label>" +
          escape(f.label) +
          '<input name="' +
          escape(f.id) +
          '" value="' +
          escape(f.value || "") +
          '"></label>';
      });
      html +=
        '<div class="modal-actions"><button type="button" class="ghost">Cancel</button><button type="submit">' +
        escape(okLabel) +
        "</button></div></form>";
      back.innerHTML = html;
      document.body.appendChild(back);
      var form = back.querySelector("form");
      back.querySelector(".ghost").addEventListener("click", function () {
        back.remove();
        resolve(null);
      });
      form.addEventListener("submit", function (ev) {
        ev.preventDefault();
        var out = {};
        fields.forEach(function (f) {
          out[f.id] = form.elements[f.id].value;
        });
        back.remove();
        resolve(out);
      });
      var first = form.querySelector("input");
      if (first) first.focus();
    });
  }

  function mountCorner() {
    if (!window.wiki || document.getElementById("wiki-corner")) return;
    var corner = document.createElement("div");
    corner.id = "wiki-corner";
    corner.innerHTML = '<button type="button" id="new-tutorial">New tutorial</button>';
    document.body.appendChild(corner);
    corner.querySelector("#new-tutorial").addEventListener("click", function () {
      askForm("New tutorial", [{ id: "title", label: "Title", value: "" }], "Create").then(function (fields) {
        if (!fields || !fields.title.trim()) return;
        window.wiki.createTutorial(fields.title.trim()).then(function (file) {
          sessionStorage.setItem("wiki-open-edit", file);
          location.hash = "#/" + encodeURIComponent(file);
          location.reload();
        }).catch(function (err) {
          showError(escape(err && err.message ? err.message : String(err)));
        });
      });
    });
  }

  var btn = document.getElementById("nav-toggle");
  var side = document.querySelector(".sidebar");
  if (btn && side) {
    btn.addEventListener("click", function () {
      side.classList.toggle("open");
    });
  }

  mountCorner();
  window.addEventListener("hashchange", route);
  route();
})();
