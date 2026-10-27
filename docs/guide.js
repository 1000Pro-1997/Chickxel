// 사용 가이드: 세부 항목(h3)마다 한 쪽씩 보여 주고(세부 항목이 없는 장은 장 하나가 한 쪽), 왼쪽 목차는 지금 장만 펼친다.
(function () {
  var en = document.documentElement.lang === 'en';
  var text = en
    ? { prev: 'Previous', next: 'Next', toggle: 'Expand or collapse', search: 'Search the guide', none: 'No results' }
    : { prev: '이전', next: '다음', toggle: '펼치기·접기', search: '가이드 검색', none: '찾은 쪽이 없습니다' };
  var chevron = '<svg class="icon" viewBox="0 0 24 24"><path d="m9 18 6-6-6-6"/></svg>';
  var content = document.querySelector('.content');
  var toc = document.querySelector('.toc');

  // macOS면 키를 앱 메뉴·도움말과 같은 ⌘ ⌥ ⇧로 보인다. 명령 단축키 칸(.key)은 메뉴처럼 붙여 쓰고(⌘⇧S), class="os-keep"인 키는 그대로.
  var platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || navigator.userAgent;
  if (/mac|iphone|ipad|ipod/i.test(platform)) {
    var macKeys = { Ctrl: '⌘', Alt: '⌥', Shift: '⇧' };
    Array.prototype.forEach.call(content.querySelectorAll('kbd:not(.os-keep)'), function (k) {
      if (macKeys[k.textContent]) k.textContent = macKeys[k.textContent];
    });
    Array.prototype.forEach.call(content.querySelectorAll('.key'), function (key) {
      Array.prototype.slice.call(key.childNodes).forEach(function (n) {
        if (n.nodeType === 3 && n.textContent === '+') key.removeChild(n);
      });
    });
  }
  var pages = [];
  var alias = {}; // 장 id → 그 장의 첫 세부 쪽 id(옛 #장 링크도 열리게)

  // 세부 항목이 있는 장은 따로 머리말 쪽을 두지 않고, h2 다음 머리말 글을 첫 세부 쪽 맨 위에 붙인다.
  Array.prototype.slice.call(content.querySelectorAll('section.doc')).forEach(function (section) {
    var chapterTitle = section.querySelector('h2').textContent;
    var nodes = Array.prototype.slice.call(section.childNodes);
    var before = [];
    var current = null;
    var mine = [];
    nodes.forEach(function (node) {
      if (node.nodeType === 1 && node.tagName === 'H3') {
        var id = node.id;
        node.removeAttribute('id');
        node.classList.add('page-title');
        if (!mine.length) alias[section.id] = id;
        current = page(id, section.id, node.textContent, chapterTitle);
        if (!mine.length) {
          var intro = before.filter(function (n) { return n.nodeType === 1 && n.tagName !== 'H2'; });
          if (intro.length) {
            var box = document.createElement('div');
            box.className = 'chapter-intro';
            intro.forEach(function (n) { box.appendChild(n); });
            current.el.appendChild(box);
          }
        }
        mine.push(current);
      }
      if (current) current.el.appendChild(node);
      else before.push(node);
    });
    if (!current) {
      current = page(section.id, section.id, chapterTitle, null);
      before.forEach(function (n) { current.el.appendChild(n); });
      mine.push(current);
    }
    section.removeAttribute('id');
    section.innerHTML = '';
    mine.forEach(function (p) { section.appendChild(p.el); });
  });

  function first(id) { return alias[id] || id; }

  function page(id, chapterId, title, chapterTitle) {
    var el = document.createElement('div');
    el.className = 'page';
    el.id = id;
    if (chapterTitle) {
      var crumb = document.createElement('a');
      crumb.className = 'crumb';
      crumb.href = '#' + first(chapterId);
      crumb.textContent = chapterTitle;
      el.appendChild(crumb);
    }
    var p = { id: id, chapterId: chapterId, title: title, chapterTitle: chapterTitle, el: el };
    pages.push(p);
    return p;
  }

  function escapeHtml(s) {
    return s.replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; });
  }

  // 쪽 아래 이전·다음 단추.
  pages.forEach(function (p, i) {
    var nav = document.createElement('nav');
    nav.className = 'pager';
    var prev = pages[i - 1], next = pages[i + 1];
    nav.innerHTML =
      (prev ? '<a class="prev" href="#' + prev.id + '"><span>' + text.prev + '</span>' + escapeHtml(prev.title) + '</a>' : '<span></span>') +
      (next ? '<a class="next" href="#' + next.id + '"><span>' + text.next + '</span>' + escapeHtml(next.title) + '</a>' : '<span></span>');
    p.el.appendChild(nav);
  });

  // 목차: 장마다 접기 단추를 붙이고 처음에는 모두 접는다. 장 이름은 그 장의 첫 쪽을 열고 펼친다.
  var groups = Array.prototype.slice.call(toc.querySelectorAll(':scope > ol > li:not(.toc-label)'));
  groups.forEach(function (li) {
    var sub = li.querySelector('ol');
    var link = li.querySelector('a');
    li.dataset.chapter = link.getAttribute('href').slice(1);
    link.setAttribute('href', '#' + first(li.dataset.chapter));
    link.addEventListener('click', function () { li.classList.add('open'); });
    var row = document.createElement('div');
    row.className = 'toc-row';
    li.insertBefore(row, link);
    row.appendChild(link);
    if (!sub) {
      // 세부 항목이 없는 장도 접기 단추 자리만큼 띄워 이름을 맞춘다.
      var spacer = document.createElement('span');
      spacer.className = 'toc-toggle toc-spacer';
      row.insertBefore(spacer, link);
      return;
    }
    var button = document.createElement('button');
    button.type = 'button';
    button.className = 'toc-toggle';
    button.title = text.toggle;
    button.setAttribute('aria-label', text.toggle);
    button.innerHTML = chevron;
    button.addEventListener('click', function () { li.classList.toggle('open'); });
    row.insertBefore(button, link);
  });

  var tocLinks = Array.prototype.slice.call(toc.querySelectorAll('ol ol a'));
  var manual = document.querySelector('.manual');
  var narrow = window.matchMedia('(max-width: 680px)');

  // 좁은 화면: 목차는 접어 두고 위 단추로 연다.
  var opener = document.createElement('button');
  opener.type = 'button';
  opener.className = 'toc-open';
  opener.innerHTML = '<svg class="icon" viewBox="0 0 24 24"><path d="M4 6h16"/><path d="M4 12h16"/><path d="M4 18h16"/></svg><span>' +
    toc.querySelector('h2').textContent + '</span><b></b>';
  opener.addEventListener('click', function () { manual.classList.toggle('toc-shown'); });
  manual.insertBefore(opener, toc);

  function show(id, scroll) {
    var target = pages.find(function (p) { return p.id === first(id); }) || pages[0];
    // 옛 #장 주소는 첫 세부 쪽 주소로 바꿔 둔다(목차·이전·다음과 같은 주소).
    if (alias[id] && location.hash === '#' + id) history.replaceState(null, '', '#' + target.id);
    pages.forEach(function (p) { p.el.classList.toggle('active', p === target); });
    tocLinks.forEach(function (a) { a.classList.toggle('active', a.getAttribute('href') === '#' + target.id); });
    groups.forEach(function (li) {
      var mine = li.dataset.chapter === target.chapterId;
      li.classList.toggle('open', mine);
      li.classList.toggle('current', mine);
    });
    // 목차 안에서만 지금 항목이 보이게 굴린다(창 전체는 움직이지 않음).
    var active = toc.querySelector('a.active') || toc.querySelector('li.current a');
    if (active && toc.scrollHeight > toc.clientHeight) {
      var a = active.getBoundingClientRect(), t = toc.getBoundingClientRect();
      if (a.top < t.top || a.bottom > t.bottom) toc.scrollTop += a.top - t.top - t.height / 3;
    }
    opener.querySelector('b').textContent = target.title;
    if (scroll) {
      if (narrow.matches) {
        manual.classList.remove('toc-shown');
        window.scrollTo(0, Math.max(0, content.getBoundingClientRect().top + window.scrollY - document.querySelector('.top').offsetHeight - opener.offsetHeight));
      } else {
        content.scrollTop = 0;
      }
    }
    document.title = target.title + (en ? ' — Gpixel guide' : ' — Gpixel 사용 가이드');
    return target;
  }

  // 주소의 #id로 브라우저가 내용 창을 굴려 놓은 것을 첫 줄로 되돌린다.
  function resetPane() {
    if (!narrow.matches) {
      content.scrollTop = 0;
      window.scrollTo(0, 0);
    } else if (location.hash) {
      window.scrollTo(0, Math.max(0, content.getBoundingClientRect().top + window.scrollY - document.querySelector('.top').offsetHeight - opener.offsetHeight));
    }
  }

  // ───────────── 검색: 목차 위 입력란. 낱말(띄어 쓴 조각)을 모두 담은 쪽을 찾고, 제목에 있거나 많이 나오는 쪽을 앞에 둔다.
  // 결과를 누르면 그 쪽을 열고 찾은 낱말을 노랗게 칠한다(입력을 지우거나 Esc를 누르면 목차로 돌아감).
  // 쪽 글(빵 부스러기·제목·이전/다음 제외). 글 덩어리(문단·칸) 사이는 띄어 붙인다.
  function plainText(el) {
    var out = [], walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT), n, lastBlock = null;
    while ((n = walker.nextNode())) {
      if (n.parentNode.closest('.crumb, .pager, .page-title')) continue;
      var block = n.parentNode.closest('p, li, td, th, figcaption, h2, h3, h4, dt, dd, div, pre');
      if (block !== lastBlock) out.push(' ');
      lastBlock = block;
      out.push(n.nodeValue);
    }
    return out.join('').replace(/\s+/g, ' ').trim();
  }
  pages.forEach(function (p) {
    p.plain = plainText(p.el);
    p.lower = p.plain.toLowerCase();
    p.lowerTitle = p.title.toLowerCase();
  });
  var searchBox = document.createElement('div');
  searchBox.className = 'guide-search';
  searchBox.innerHTML = '<svg class="icon" viewBox="0 0 24 24"><circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/></svg><input type="search" autocomplete="off" spellcheck="false">';
  var searchInput = searchBox.querySelector('input');
  searchInput.placeholder = text.search;
  searchInput.setAttribute('aria-label', text.search);
  var resultList = document.createElement('ol');
  resultList.className = 'search-results';
  toc.insertBefore(searchBox, toc.firstChild);
  toc.insertBefore(resultList, searchBox.nextSibling);
  var searchTerms = [];
  var selected = -1;

  function termsOf(q) { return q.toLowerCase().split(/\s+/).filter(function (t) { return t; }); }

  function count(hay, t) {
    for (var n = 0, i = hay.indexOf(t); i >= 0; i = hay.indexOf(t, i + t.length)) n++;
    return n;
  }

  function search(terms) {
    return pages.map(function (p) {
      var score = 0;
      for (var i = 0; i < terms.length; i++) {
        var t = terms[i], inTitle = p.lowerTitle.indexOf(t) >= 0, n = count(p.lower, t);
        if (!inTitle && !n) return null;
        score += (inTitle ? 100 : 0) + Math.min(n, 20);
      }
      return { page: p, score: score };
    }).filter(function (r) { return r; }).sort(function (a, b) { return b.score - a.score; }).slice(0, 60);
  }

  function termPattern(terms) {
    return new RegExp(terms.map(function (t) { return t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }).join('|'), 'gi');
  }

  function marked(s, terms) {
    var re = termPattern(terms), out = '', last = 0, m;
    while ((m = re.exec(s))) {
      out += escapeHtml(s.slice(last, m.index)) + '<mark>' + escapeHtml(m[0]) + '</mark>';
      last = m.index + m[0].length;
    }
    return out + escapeHtml(s.slice(last));
  }

  function snippet(p, terms) {
    var at = -1;
    terms.forEach(function (t) { var i = p.lower.indexOf(t); if (i >= 0 && (at < 0 || i < at)) at = i; });
    if (at < 0) return '';
    var from = Math.max(0, at - 30), to = Math.min(p.plain.length, at + 90);
    return (from > 0 ? '…' : '') + marked(p.plain.slice(from, to), terms) + (to < p.plain.length ? '…' : '');
  }

  function renderResults() {
    searchTerms = termsOf(searchInput.value);
    selected = -1;
    toc.classList.toggle('searching', searchTerms.length > 0);
    if (!searchTerms.length) {
      resultList.innerHTML = '';
      clearHits();
      return;
    }
    var found = search(searchTerms);
    resultList.innerHTML = found.length
      ? found.map(function (r) {
        var p = r.page;
        return '<li><a href="#' + p.id + '"><b>' + marked(p.title, searchTerms) + '</b>' +
          (p.chapterTitle && p.chapterTitle !== p.title ? '<span class="where">' + escapeHtml(p.chapterTitle) + '</span>' : '') +
          '<span class="snip">' + snippet(p, searchTerms) + '</span></a></li>';
      }).join('')
      : '<li class="empty">' + text.none + '</li>';
  }

  function select(i) {
    var links = resultList.querySelectorAll('a');
    if (!links.length) return;
    selected = (i + links.length) % links.length;
    Array.prototype.forEach.call(links, function (a, k) { a.classList.toggle('selected', k === selected); });
    links[selected].scrollIntoView({ block: 'nearest' });
  }

  function clearHits() {
    Array.prototype.forEach.call(content.querySelectorAll('mark.hit'), function (m) {
      var parent = m.parentNode;
      parent.replaceChild(document.createTextNode(m.textContent), m);
      parent.normalize();
    });
  }

  // 지금 쪽에서 찾은 낱말을 칠하고 첫 곳으로 굴린다.
  function showHits(page) {
    clearHits();
    if (!searchTerms.length) return;
    var re = termPattern(searchTerms), probe = new RegExp(re.source, 'i');
    var walker = document.createTreeWalker(page.el, NodeFilter.SHOW_TEXT), nodes = [], n;
    while ((n = walker.nextNode())) if (!n.parentNode.closest('.pager, .crumb') && probe.test(n.nodeValue)) nodes.push(n);
    nodes.forEach(function (node) {
      var s = node.nodeValue, frag = document.createDocumentFragment(), last = 0, m;
      re.lastIndex = 0;
      while ((m = re.exec(s))) {
        frag.appendChild(document.createTextNode(s.slice(last, m.index)));
        var mark = document.createElement('mark');
        mark.className = 'hit';
        mark.textContent = m[0];
        frag.appendChild(mark);
        last = m.index + m[0].length;
      }
      frag.appendChild(document.createTextNode(s.slice(last)));
      node.parentNode.replaceChild(frag, node);
    });
    var firstHit = page.el.querySelector('mark.hit');
    if (firstHit) firstHit.scrollIntoView({ block: 'center' });
  }

  searchInput.addEventListener('input', renderResults);
  searchInput.addEventListener('keydown', function (e) {
    if (e.key === 'ArrowDown') { select(selected + 1); e.preventDefault(); }
    else if (e.key === 'ArrowUp') { select(selected - 1); e.preventDefault(); }
    else if (e.key === 'Enter') {
      var link = resultList.querySelectorAll('a')[Math.max(0, selected)];
      if (link) location.hash = link.getAttribute('href');
      e.preventDefault();
    } else if (e.key === 'Escape') {
      searchInput.value = '';
      renderResults();
    }
  });

  // 화면 그림은 내용 폭에 맞춰 줄어 보이므로 누르면 원래 크기로 새 탭에 연다.
  content.addEventListener('click', function (e) {
    var img = e.target.closest('.shot img');
    if (img) window.open(img.currentSrc || img.src, '_blank', 'noopener');
  });

  window.addEventListener('hashchange', function () {
    var target = show(location.hash.slice(1), true);
    resetPane();
    showHits(target);
  });
  show(location.hash.slice(1), false);
  resetPane();
  window.addEventListener('load', resetPane);
})();
