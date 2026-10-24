// 사용 가이드: 장(h2)과 세부 항목(h3)마다 한 쪽씩 보여 주고, 왼쪽 목차는 지금 장만 펼친다.
(function () {
  var en = document.documentElement.lang === 'en';
  var text = en
    ? { contents: 'In this chapter', prev: 'Previous', next: 'Next', toggle: 'Expand or collapse' }
    : { contents: '이 장의 내용', prev: '이전', next: '다음', toggle: '펼치기·접기' };
  var chevron = '<svg class="icon" viewBox="0 0 24 24"><path d="m9 18 6-6-6-6"/></svg>';
  var content = document.querySelector('.content');
  var toc = document.querySelector('.toc');
  var pages = [];

  // 장마다 머리말 쪽(h2 + 첫 h3 전까지) + h3마다 한 쪽으로 나눈다.
  Array.prototype.slice.call(content.querySelectorAll('section.doc')).forEach(function (section) {
    var chapterTitle = section.querySelector('h2').textContent;
    var nodes = Array.prototype.slice.call(section.childNodes);
    var intro = page(section.id, section.id, chapterTitle, null);
    var current = intro;
    var subs = [];
    nodes.forEach(function (node) {
      if (node.nodeType === 1 && node.tagName === 'H3') {
        var id = node.id;
        node.removeAttribute('id');
        node.classList.add('page-title');
        current = page(id, section.id, node.textContent, chapterTitle);
        subs.push(current);
      }
      current.el.appendChild(node);
    });
    if (subs.length) {
      var box = document.createElement('div');
      box.className = 'chapter-contents';
      box.innerHTML = '<h3>' + text.contents + '</h3><ol>' + subs.map(function (p) {
        return '<li><a href="#' + p.id + '">' + escapeHtml(p.title) + '</a></li>';
      }).join('') + '</ol>';
      intro.el.appendChild(box);
    }
    section.removeAttribute('id');
    section.innerHTML = '';
    section.appendChild(intro.el);
    subs.forEach(function (p) { section.appendChild(p.el); });
  });

  function page(id, chapterId, title, chapterTitle) {
    var el = document.createElement('div');
    el.className = 'page';
    el.id = id;
    if (chapterTitle) {
      var crumb = document.createElement('a');
      crumb.className = 'crumb';
      crumb.href = '#' + chapterId;
      crumb.textContent = chapterTitle;
      el.appendChild(crumb);
    }
    var p = { id: id, chapterId: chapterId, title: title, el: el };
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

  // 목차: 장마다 접기 단추를 붙이고 처음에는 모두 접는다.
  var groups = Array.prototype.slice.call(toc.querySelectorAll(':scope > ol > li'));
  groups.forEach(function (li) {
    var sub = li.querySelector('ol');
    var link = li.querySelector('a');
    var row = document.createElement('div');
    row.className = 'toc-row';
    li.insertBefore(row, link);
    row.appendChild(link);
    if (!sub) return;
    var button = document.createElement('button');
    button.type = 'button';
    button.className = 'toc-toggle';
    button.title = text.toggle;
    button.setAttribute('aria-label', text.toggle);
    button.innerHTML = chevron;
    button.addEventListener('click', function () { li.classList.toggle('open'); });
    row.insertBefore(button, link);
  });

  var tocLinks = Array.prototype.slice.call(toc.querySelectorAll('a'));
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
    var target = pages.find(function (p) { return p.id === id; }) || pages[0];
    pages.forEach(function (p) { p.el.classList.toggle('active', p === target); });
    tocLinks.forEach(function (a) { a.classList.toggle('active', a.getAttribute('href') === '#' + target.id); });
    groups.forEach(function (li) {
      var mine = li.querySelector('a').getAttribute('href') === '#' + target.chapterId;
      li.classList.toggle('open', mine);
      li.classList.toggle('current', mine);
    });
    // 목차 안에서만 지금 항목이 보이게 굴린다(창 전체는 움직이지 않음).
    var active = toc.querySelector('a.active');
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

  window.addEventListener('hashchange', function () {
    show(location.hash.slice(1), true);
    resetPane();
  });
  show(location.hash.slice(1), false);
  resetPane();
  window.addEventListener('load', resetPane);
})();
