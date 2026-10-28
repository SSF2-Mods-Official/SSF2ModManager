/* Minimal Markdown → HTML for SSF2 Shell docs (no CDN). */
(function (global) {
  function escapeHtml(s) {
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function rewriteHref(href) {
    if (!href || /^(https?:|mailto:|#)/i.test(href)) return href;
    if (/\.md$/i.test(href)) {
      return "#md-rel:" + encodeURIComponent(href.replace(/\\/g, "/"));
    }
    return href;
  }

  function inline(text) {
    var s = escapeHtml(text);
    s = s.replace(/`([^`]+)`/g, "<code>$1</code>");
    s = s.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
    s = s.replace(/\[([^\]]+)\]\(([^)]+)\)/g, function (_, label, href) {
      return '<a href="' + rewriteHref(href) + '">' + label + "</a>";
    });
    return s;
  }

  function slug(text) {
    var s = String(text)
      .replace(/`([^`]+)`/g, "$1")
      .replace(/\*\*([^*]+)\*\*/g, "$1")
      .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1")
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-|-$/g, "");
    return s || "section";
  }

  function renderMarkdown(md) {
    var lines = md.replace(/\r\n/g, "\n").split("\n");
    var html = [];
    var i = 0;
    var inUl = false, inOl = false, inTable = false, inCode = false;
    var codeLang = "";
    var codeBuf = [];
    var pendingTable = "";
    var pendingList = "";
    var usedIds = {};

    function headingId(text) {
      var base = slug(text);
      var id = base;
      var n = 2;
      while (usedIds[id]) {
        id = base + "-" + n;
        n++;
      }
      usedIds[id] = true;
      pendingTable = id === "info" ? "infobox" : "";
      pendingList = id === "contents" ? "toc" : "";
      return id;
    }

    function dropPending() {
      pendingTable = "";
      pendingList = "";
    }

    function closeLists() {
      if (inUl) { html.push("</ul>"); inUl = false; }
      if (inOl) { html.push("</ol>"); inOl = false; }
    }
    function closeTable() {
      if (inTable) { html.push("</tbody></table>"); inTable = false; }
    }

    while (i < lines.length) {
      var line = lines[i];

      if (inCode) {
        if (/^```/.test(line)) {
          html.push("<pre><code>" + escapeHtml(codeBuf.join("\n")) + "</code></pre>");
          codeBuf = [];
          inCode = false;
          i++;
          continue;
        }
        codeBuf.push(line);
        i++;
        continue;
      }

      var fence = line.match(/^```([\w-]*)\s*$/);
      if (fence) {
        closeLists();
        closeTable();
        dropPending();
        inCode = true;
        codeLang = fence[1] || "";
        i++;
        continue;
      }

      if (/^\s*$/.test(line)) {
        closeLists();
        closeTable();
        i++;
        continue;
      }

      if (/^>\s?/.test(line)) {
        closeLists();
        closeTable();
        dropPending();
        var note = line.replace(/^>\s?/, "");
        while (i + 1 < lines.length && /^>\s?/.test(lines[i + 1])) {
          i++;
          note += " " + lines[i].replace(/^>\s?/, "");
        }
        html.push('<div class="tip">' + inline(note) + "</div>");
        i++;
        continue;
      }

      var details = line.match(/^:::details(?:\s+(.+))?\s*$/);
      if (details) {
        closeLists();
        closeTable();
        dropPending();
        var summary = details[1] || "Details";
        var buf = [];
        i++;
        while (i < lines.length && !/^:::\s*$/.test(lines[i])) {
          buf.push(lines[i]);
          i++;
        }
        if (i < lines.length) i++;
        html.push(
          '<details class="fold"><summary>' +
            inline(summary) +
            "</summary>" +
            renderMarkdown(buf.join("\n")) +
            "</details>"
        );
        continue;
      }

      var hm = line.match(/^(#{1,3})\s+(.+)$/);
      if (hm) {
        closeLists();
        closeTable();
        var level = hm[1].length;
        var hid = headingId(hm[2]);
        html.push("<h" + level + ' id="' + hid + '">' + inline(hm[2]) + "</h" + level + ">");
        i++;
        continue;
      }

      if (/^\|/.test(line) && i + 1 < lines.length && /^\|?\s*-+/.test(lines[i + 1])) {
        closeLists();
        var header = line.split("|").slice(1, -1).map(function (c) { return c.trim(); });
        i += 2;
        var editHalf = header.some(function (c) { return c.indexOf("Edit from the official SWF") >= 0; });
        var tableClass = pendingTable;
        pendingTable = "";
        pendingList = "";
        var classes = [];
        if (editHalf) classes.push("edit-half");
        if (tableClass) classes.push(tableClass);
        html.push("<table" + (classes.length ? ' class="' + classes.join(" ") + '"' : "") + "><thead><tr>");
        header.forEach(function (c) {
          var span = tableClass === "infobox" && header.length === 1 ? ' colspan="2"' : "";
          html.push("<th" + span + ">" + inline(c) + "</th>");
        });
        html.push("</tr></thead><tbody>");
        inTable = true;
        while (i < lines.length && /^\|/.test(lines[i])) {
          var cells = lines[i].split("|").slice(1, -1).map(function (c) { return c.trim(); });
          html.push("<tr>");
          cells.forEach(function (c) { html.push("<td>" + inline(c) + "</td>"); });
          html.push("</tr>");
          i++;
        }
        closeTable();
        continue;
      }

      var ul = line.match(/^[-*]\s+(.+)$/);
      if (ul) {
        closeTable();
        if (inOl) { html.push("</ol>"); inOl = false; }
        if (!inUl) { dropPending(); html.push("<ul>"); inUl = true; }
        html.push("<li>" + inline(ul[1]) + "</li>");
        i++;
        continue;
      }

      var ol = line.match(/^\d+\.\s+(.+)$/);
      if (ol) {
        closeTable();
        if (inUl) { html.push("</ul>"); inUl = false; }
        if (!inOl) {
          var listClass = pendingList;
          pendingList = "";
          pendingTable = "";
          html.push(listClass ? '<ol class="' + listClass + '">' : "<ol>");
          inOl = true;
        }
        html.push("<li>" + inline(ol[1]) + "</li>");
        i++;
        continue;
      }

      var imgLine = line.match(/^!\[([^\]]*)\]\(([^)]+)\)\s*$/);
      if (imgLine) {
        closeLists();
        closeTable();
        dropPending();
        var imgSrc = imgLine[2].trim();
        if (!/^[a-z]+:/i.test(imgSrc)) {
          html.push('<p><img alt="' + escapeHtml(imgLine[1]) + '" data-rel="' + escapeHtml(imgSrc) + '"></p>');
        }
        i++;
        continue;
      }

      closeLists();
      closeTable();
      dropPending();
      html.push("<p>" + inline(line) + "</p>");
      i++;
    }

    closeLists();
    closeTable();
    if (inCode) {
      html.push("<pre><code>" + escapeHtml(codeBuf.join("\n")) + "</code></pre>");
    }
    return html.join("\n");
  }

  function parseFrontMatter(raw) {
    var m = raw.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n([\s\S]*)$/);
    if (!m) return { meta: {}, body: raw };
    var meta = {};
    m[1].split(/\r?\n/).forEach(function (line) {
      var kv = line.match(/^(\w+)\s*:\s*(.*)$/);
      if (!kv) return;
      var key = kv[1];
      var val = kv[2].trim().replace(/^["']|["']$/g, "");
      if (key === "order") meta.order = parseInt(val, 10) || 100;
      else meta[key] = val;
    });
    return { meta: meta, body: m[2] };
  }

  global.Ssf2DocsMd = { renderMarkdown: renderMarkdown, parseFrontMatter: parseFrontMatter };
})(window);
