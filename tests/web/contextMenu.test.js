const fs = require('fs');
const path = require('path');
const vm = require('vm');
const { JSDOM } = require('jsdom');

const SCRIPT = fs.readFileSync(
    path.join(__dirname, '..', '..', 'Jellyfin.Plugin.PublicMediaLinks', 'Web', 'contextMenu.js'),
    'utf8');

let failures = 0;
function check(name, cond, extra) {
    if (cond) {
        console.log('  PASS ' + name);
    } else {
        failures++;
        console.log('  FAIL ' + name + (extra ? ' -> ' + extra : ''));
    }
}

// Mirrors the markup jellyfin-web's actionSheet.ts emits.
function actionSheetHtml() {
    return `
    <div class="actionSheet actionsheet-not-fullscreen">
      <div class="actionSheetContent">
        <div class="actionSheetScroller">
          <button is="emby-button" type="button" class="listItem listItem-button actionSheetMenuItem" data-id="download">
            <span class="actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons file_download"></span>
            <div class="listItemBody actionsheetListItemBody">
              <div class="listItemBodyText actionSheetItemText">Download</div>
            </div>
          </button>
          <button is="emby-button" type="button" class="listItem listItem-button actionSheetMenuItem" data-id="copy-stream">
            <span class="actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons content_copy"></span>
            <div class="listItemBody actionsheetListItemBody">
              <div class="listItemBodyText actionSheetItemText">Copy media URL</div>
            </div>
          </button>
        </div>
      </div>
    </div>`;
}

async function harness({ isAdmin = true, url = 'http://jf.test/web/index.html#/home' } = {}) {
    const dom = new JSDOM('<!DOCTYPE html><html><body><div id="app"></div></body></html>', {
        url,
        runScripts: 'outside-only',
        pretendToBeVisual: true
    });

    const win = dom.window;
    const calls = [];

    win.ApiClient = {
        getCurrentUser: () => Promise.resolve({ Policy: { IsAdministrator: isAdmin } }),
        getUrl: (p) => 'http://jf.test/' + p,
        ajax: (opts) => {
            calls.push(JSON.parse(opts.data));
            return Promise.resolve({
                WatchUrl: 'http://jf.test/PublicMediaLinks/w/TOKEN123',
                ExpiresUtc: '2026-09-13T12:00:00Z'
            });
        }
    };

    const copied = [];
    win.navigator.clipboard = { writeText: (t) => { copied.push(t); return Promise.resolve(); } };

    vm.runInContext(SCRIPT, dom.getInternalVMContext());

    // Let refreshAdmin's promise settle.
    await new Promise(r => setTimeout(r, 0));
    await new Promise(r => setTimeout(r, 0));

    return { win, doc: win.document, calls, copied };
}

function openSheet(doc) {
    const holder = doc.createElement('div');
    holder.innerHTML = actionSheetHtml();
    const sheet = holder.firstElementChild;
    doc.body.appendChild(sheet);
    return sheet;
}

async function tick() {
    for (let i = 0; i < 5; i++) await new Promise(r => setTimeout(r, 0));
}

function clickCard(doc, id) {
    const card = doc.createElement('div');
    card.className = 'card';
    card.setAttribute('data-id', id);
    const btn = doc.createElement('button');
    card.appendChild(btn);
    doc.body.appendChild(card);
    btn.dispatchEvent(new doc.defaultView.MouseEvent('click', { bubbles: true }));
    return card;
}

(async () => {
    console.log('\n1. Injects the menu row next to "Copy media URL"');
    {
        const { doc } = await harness();
        const sheet = openSheet(doc);
        await tick();

        const row = sheet.querySelector('[data-id="pml-copy-share"]');
        check('row added', !!row);
        check('label correct', row && row.querySelector('.actionSheetItemText').textContent === 'Copy Public Share Link');
        check('icon swapped', row && row.querySelector('.actionsheetMenuItemIcon').classList.contains('link')
            && !row.querySelector('.actionsheetMenuItemIcon').classList.contains('content_copy'));
        check('inserted directly after copy-stream',
            row && row.previousElementSibling.getAttribute('data-id') === 'copy-stream');
    }

    console.log('\n2. Hidden from non-administrators');
    {
        const { doc } = await harness({ isAdmin: false });
        const sheet = openSheet(doc);
        await tick();
        check('no row for non-admin', !sheet.querySelector('[data-id="pml-copy-share"]'));
    }

    console.log('\n3. Absent when the item is not downloadable (no copy-stream row)');
    {
        const { doc } = await harness();
        const holder = doc.createElement('div');
        holder.innerHTML = `<div class="actionSheet"><button class="listItem listItem-button actionSheetMenuItem" data-id="refresh"><div class="listItemBodyText actionSheetItemText">Refresh</div></button></div>`;
        doc.body.appendChild(holder.firstElementChild);
        await tick();
        check('no row without copy-stream', !doc.querySelector('[data-id="pml-copy-share"]'));
    }

    console.log('\n4. Uses the card that opened the menu');
    {
        const { doc, calls, copied } = await harness();
        clickCard(doc, 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa');
        const sheet = openSheet(doc);
        await tick();

        sheet.querySelector('[data-id="pml-copy-share"]')
            .dispatchEvent(new doc.defaultView.MouseEvent('click', { bubbles: true }));
        await tick();

        check('posted the clicked card id', calls.length === 1 && calls[0].ItemId === 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
            JSON.stringify(calls));
        check('copied the watch url', copied[0] === 'http://jf.test/PublicMediaLinks/w/TOKEN123', JSON.stringify(copied));
        check('omits TtlHours so server default applies', calls[0] && !('TtlHours' in calls[0]));
    }

    console.log('\n5. Falls back to the details-page URL when no card was clicked');
    {
        const { doc, calls } = await harness({ url: 'http://jf.test/web/index.html#/details?id=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb&serverId=x' });
        const sheet = openSheet(doc);
        await tick();
        sheet.querySelector('[data-id="pml-copy-share"]')
            .dispatchEvent(new doc.defaultView.MouseEvent('click', { bubbles: true }));
        await tick();
        check('used id from URL', calls.length === 1 && calls[0].ItemId === 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', JSON.stringify(calls));
    }

    console.log('\n6. REGRESSION: a stale grid card must not win over the details page');
    {
        const { doc, calls } = await harness({ url: 'http://jf.test/web/index.html#/details?id=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' });
        // User clicks a card in a grid...
        clickCard(doc, 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa');
        // ...then opens the page-level "..." button, which has no GUID ancestor.
        const moreBtn = doc.createElement('button');
        moreBtn.className = 'btnMoreCommands';
        doc.body.appendChild(moreBtn);
        moreBtn.dispatchEvent(new doc.defaultView.MouseEvent('click', { bubbles: true }));

        const sheet = openSheet(doc);
        await tick();
        sheet.querySelector('[data-id="pml-copy-share"]')
            .dispatchEvent(new doc.defaultView.MouseEvent('click', { bubbles: true }));
        await tick();

        check('used the details page item, not the stale card',
            calls.length === 1 && calls[0].ItemId === 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', JSON.stringify(calls));
    }

    console.log('\n7. REGRESSION: clicking our own row must not clear the remembered id');
    {
        const { doc, calls } = await harness();
        clickCard(doc, 'cccccccccccccccccccccccccccccccc');
        const sheet = openSheet(doc);
        await tick();
        sheet.querySelector('[data-id="pml-copy-share"]')
            .dispatchEvent(new doc.defaultView.MouseEvent('click', { bubbles: true }));
        await tick();
        check('id survived the menu click', calls.length === 1 && calls[0].ItemId === 'cccccccccccccccccccccccccccccccc', JSON.stringify(calls));
    }

    console.log('\n8. Not added twice if the sheet is rescanned');
    {
        const { doc } = await harness();
        const sheet = openSheet(doc);
        await tick();
        doc.body.appendChild(doc.createElement('div')); // trigger observer again
        sheet.appendChild(doc.createElement('span'));
        await tick();
        check('exactly one row', sheet.querySelectorAll('[data-id="pml-copy-share"]').length === 1);
    }

    console.log(failures === 0 ? '\nALL PASS' : '\n' + failures + ' FAILURE(S)');
    process.exit(failures === 0 ? 0 : 1);
})();
