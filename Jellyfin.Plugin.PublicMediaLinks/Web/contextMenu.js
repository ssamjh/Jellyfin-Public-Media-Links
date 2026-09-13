/*
 * Public Media Links - item context menu integration.
 *
 * Injected into jellyfin-web's index.html by the File Transformation plugin, so it is
 * served to every client without modifying any file on disk.
 *
 * This works against the rendered action sheet DOM rather than the minified web bundle.
 * Patching the bundle by regex is pinned to one exact build of jellyfin-web; the DOM
 * structure is comparatively stable.
 */

(function () {
    'use strict';

    var CONFIG = {
        // Hours until the link expires. null uses the plugin's configured default (3h).
        ttlHours: null,
        // Text shown in the context menu.
        label: 'Copy Public Share Link',
        // Material icon name for the menu row.
        icon: 'link'
    };

    var MENU_ID = 'pml-copy-share';
    var GUID = /^[0-9a-f]{32}$/i;

    var lastItemId = null;
    var isAdmin = null;

    // ---------------------------------------------------------------- helpers

    function isGuid(value) {
        return typeof value === 'string' && GUID.test(value.replace(/-/g, ''));
    }

    /** Reads the item id off the details page URL, e.g. #/details?id=<guid>. */
    function itemIdFromUrl() {
        var sources = [];

        var hash = window.location.hash || '';
        var q = hash.indexOf('?');
        if (q !== -1) {
            sources.push(hash.slice(q + 1));
        }
        sources.push(window.location.search.replace(/^\?/, ''));

        for (var i = 0; i < sources.length; i++) {
            var id = new URLSearchParams(sources[i]).get('id');
            if (isGuid(id)) {
                return id;
            }
        }

        return null;
    }

    /**
     * The action sheet DOM carries no item id, so remember which card was clicked just
     * before it opened.
     *
     * This always overwrites, including with null. Remembering only the last GUID ever
     * seen would mean clicking a card in a grid, navigating to some other item's detail
     * page and opening its menu would share the card from the grid instead.
     */
    function rememberItem(e) {
        var target = e.target;

        // Clicks inside an open menu must not clobber the id the menu was opened for.
        if (target && target.closest && target.closest('.actionSheet')) {
            return;
        }

        var node = target;
        var found = null;

        while (node && node !== document) {
            if (node.getAttribute) {
                var id = node.getAttribute('data-id');
                if (isGuid(id)) {
                    found = id;
                    break;
                }
            }
            node = node.parentNode;
        }

        lastItemId = found;
    }

    document.addEventListener('pointerdown', rememberItem, true);
    document.addEventListener('click', rememberItem, true);

    function toast(message) {
        var box = document.createElement('div');
        box.textContent = message;
        box.style.cssText = [
            'position:fixed', 'left:50%', 'bottom:48px', 'transform:translateX(-50%)',
            'background:#303030', 'color:#fff', 'padding:12px 20px', 'border-radius:6px',
            'font:14px/1.4 system-ui,sans-serif', 'z-index:999999', 'max-width:80vw',
            'box-shadow:0 2px 12px rgba(0,0,0,.5)', 'transition:opacity .3s', 'opacity:1'
        ].join(';');

        document.body.appendChild(box);
        setTimeout(function () { box.style.opacity = '0'; }, 2600);
        setTimeout(function () { box.remove(); }, 3000);
    }

    /** Shows the URL when the clipboard is unavailable, so the link is never just lost. */
    function showFallback(text) {
        var wrap = document.createElement('div');
        wrap.style.cssText = [
            'position:fixed', 'inset:0', 'background:rgba(0,0,0,.7)', 'z-index:999999',
            'display:flex', 'align-items:center', 'justify-content:center'
        ].join(';');

        var panel = document.createElement('div');
        panel.style.cssText = [
            'background:#202020', 'color:#eee', 'padding:20px', 'border-radius:8px',
            'max-width:min(640px,90vw)', 'font:14px/1.5 system-ui,sans-serif'
        ].join(';');

        var title = document.createElement('div');
        title.textContent = 'Public share link';
        title.style.cssText = 'font-weight:600;margin-bottom:10px;';

        var input = document.createElement('input');
        input.type = 'text';
        input.readOnly = true;
        input.value = text;
        input.style.cssText = 'width:100%;padding:8px;background:#111;color:#eee;border:1px solid #444;border-radius:4px;';

        panel.appendChild(title);
        panel.appendChild(input);
        wrap.appendChild(panel);

        wrap.addEventListener('click', function (e) {
            if (e.target === wrap) {
                wrap.remove();
            }
        });

        document.body.appendChild(wrap);
        input.focus();
        input.select();
    }

    function copyText(text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(text);
        }

        // Older browsers, and any context where the async clipboard is blocked.
        return new Promise(function (resolve, reject) {
            var area = document.createElement('textarea');
            area.value = text;
            area.style.cssText = 'position:fixed;top:-1000px;opacity:0;';
            document.body.appendChild(area);
            area.select();

            var ok = false;
            try {
                ok = document.execCommand('copy');
            } catch (err) {
                ok = false;
            }

            area.remove();
            if (ok) {
                resolve();
            } else {
                reject(new Error('copy failed'));
            }
        });
    }

    // ------------------------------------------------------------------- api

    function createLink(itemId) {
        var body = { ItemId: itemId };
        if (CONFIG.ttlHours) {
            body.TtlHours = CONFIG.ttlHours;
        }

        return window.ApiClient.ajax({
            type: 'POST',
            url: window.ApiClient.getUrl('PublicMediaLinks/Links'),
            data: JSON.stringify(body),
            contentType: 'application/json',
            dataType: 'json'
        });
    }

    /**
     * Only caches a positive result. Before login getCurrentUser rejects, and caching
     * that would hide the menu item for the rest of the session.
     */
    function refreshAdmin() {
        if (isAdmin === true) {
            return;
        }

        window.ApiClient.getCurrentUser().then(function (user) {
            if (user && user.Policy && user.Policy.IsAdministrator) {
                isAdmin = true;
            }
        }).catch(function () { /* not signed in yet */ });
    }

    // ------------------------------------------------------------------ menu

    function onMenuClick() {
        // Let the click bubble on to the action sheet so it closes itself. The sheet
        // resolves with our unknown id and itemContextMenu rejects, which is the same
        // path as pressing Cancel and is already handled by every caller.
        var itemId = lastItemId || itemIdFromUrl();

        if (!itemId) {
            toast('Could not work out which item that was.');
            return;
        }

        createLink(itemId).then(function (link) {
            return copyText(link.WatchUrl).then(function () {
                var expires = new Date(link.ExpiresUtc);
                toast('Share link copied. Expires ' + expires.toLocaleString() + '.');
            }, function () {
                showFallback(link.WatchUrl);
            });
        }, function (response) {
            if (response && response.status === 403) {
                toast('Only administrators can create share links.');
            } else if (response && response.status === 400) {
                toast('That item cannot be shared.');
            } else {
                toast('Could not create a share link.');
            }
        });
    }

    function addMenuItem(sheet) {
        if (isAdmin !== true) {
            refreshAdmin();
            return;
        }

        if (sheet.querySelector('[data-id="' + MENU_ID + '"]')) {
            return;
        }

        // Piggy-back on the built-in "Copy media URL" row: it only exists when the item
        // is a downloadable file, which is exactly when a share link makes sense. Cloning
        // it also inherits whatever classes this layout (mobile, TV) applied.
        var template = sheet.querySelector('.actionSheetMenuItem[data-id="copy-stream"]');
        if (!template) {
            return;
        }

        var row = template.cloneNode(true);
        row.setAttribute('data-id', MENU_ID);

        var icon = row.querySelector('.actionsheetMenuItemIcon');
        if (icon) {
            icon.classList.remove('content_copy');
            icon.classList.add(CONFIG.icon);
        }

        var text = row.querySelector('.actionSheetItemText');
        if (text) {
            text.textContent = CONFIG.label;
        }

        row.addEventListener('click', onMenuClick);
        template.parentNode.insertBefore(row, template.nextSibling);
    }

    function scan(node) {
        if (node.nodeType !== 1) {
            return;
        }

        if (node.classList.contains('actionSheet')) {
            addMenuItem(node);
            return;
        }

        var nested = node.querySelectorAll ? node.querySelectorAll('.actionSheet') : [];
        for (var i = 0; i < nested.length; i++) {
            addMenuItem(nested[i]);
        }
    }

    // ------------------------------------------------------------------ boot

    function start() {
        new MutationObserver(function (mutations) {
            for (var i = 0; i < mutations.length; i++) {
                var added = mutations[i].addedNodes;
                for (var j = 0; j < added.length; j++) {
                    scan(added[j]);
                }
            }
        }).observe(document.body, { childList: true, subtree: true });
    }

    function waitForApiClient(attempt) {
        if (window.ApiClient && window.ApiClient.getCurrentUser) {
            refreshAdmin();
            window.addEventListener('hashchange', function () {
                // Navigating away invalidates whatever card was last clicked.
                lastItemId = null;
                refreshAdmin();
            });
            start();
            return;
        }

        // Jellyfin is an SPA; ApiClient appears some time after document-idle.
        if ((attempt || 0) < 60) {
            setTimeout(function () { waitForApiClient((attempt || 0) + 1); }, 500);
        }
    }

    waitForApiClient(0);
})();
