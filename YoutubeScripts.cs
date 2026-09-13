namespace YoutubeCounterApp
{
    public static class YouTubeScripts
    {
        public const string SelectLive = @"
            (() => {
                const rows = Array.from(document.querySelectorAll('ytcp-video-row'));
                const liveRow = rows.find(row => {
                    const t = row.innerText;
                    return (t.includes('ライブ') || t.includes('Live') || t.includes('配信中')) && !t.includes('近日配信');
                });
                if (liveRow) {
                    const titleLink = liveRow.querySelector('#video-title') || liveRow.querySelector('a');
                    if (titleLink) { titleLink.click(); return true; }
                }
                return false;
            })();
        ";

        public const string ExtractVideoId = @"
            (() => {
                const extractVParam = (text) => {
                    if (!text) return '';
                    const match = text.match(/[?&]v=([a-zA-Z0-9_-]{11})/);
                    if (match && match[1]) return match[1];

                    const matchPath = text.match(/(?:youtu\.be\/|live\/|embed\/|video\/)([a-zA-Z0-9_-]{11})/);
                    if (matchPath && matchPath[1]) return matchPath[1];

                    return '';
                };

                let id = extractVParam(window.location.href);
                if (id) return id;

                const iframes = Array.from(document.querySelectorAll('iframe'));
                for (let iframe of iframes) {
                    id = extractVParam(iframe.src);
                    if (id) return id;
                }

                const canonical = document.querySelector('link[rel=""canonical""]');
                if (canonical && canonical.href) {
                    id = extractVParam(canonical.href);
                    if (id) return id;
                }

                const links = Array.from(document.querySelectorAll('a[href]'));
                for (let link of links) {
                    id = extractVParam(link.href);
                    if (id) return id;
                }

                const inputs = Array.from(document.querySelectorAll('input, textarea'));
                for (let input of inputs) {
                    id = extractVParam(input.value);
                    if (id) return id;
                }

                return '';
            })();
        ";

        public const string ClickSeeLiveCount = @"
            (() => {
                const findAndClickBtn = (root) => {
                    if (!root) return false;
                    const directTarget = root.querySelector('#see-explore-subscribers-link, #see_explore_subscribers_button, [id*=""see-explore-subscribers""]');
                    if (directTarget) { directTarget.click(); return true; }

                    const elements = Array.from(root.querySelectorAll('a, button, ytcp-button, ytcp-button-shape'));
                    for (let el of elements) {
                        const txt = el.innerText || el.textContent || '';
                        if (txt.includes('現在の数を表示')) {
                            (el.closest('a') || el).click();
                            return true;
                        }
                    }

                    for (let child of Array.from(root.querySelectorAll('*'))) {
                        if (child.shadowRoot && findAndClickBtn(child.shadowRoot)) return true;
                    }
                    return false;
                };
                return findAndClickBtn(document);
            })();
        ";

        public const string GetSubscriberCount = @"
            (() => {
                const getShadowSubCount = (root) => {
                    if (!root) return '';
                    const counterHost = root.querySelector('yta-smooth-counter, #counter');
                    if (counterHost) {
                        let digitsStr = '';
                        for (let node of Array.from(counterHost.querySelectorAll('*'))) {
                            if (node.children.length === 0 && node.textContent.trim()) {
                                const num = node.textContent.replace(/[^0-9]/g, '');
                                if (num) digitsStr += num;
                            }
                        }
                        if (digitsStr) return digitsStr;
                    }
                    for (let child of Array.from(root.querySelectorAll('*'))) {
                        if (child.shadowRoot) {
                            const res = getShadowSubCount(child.shadowRoot);
                            if (res) return res;
                        }
                    }
                    return '';
                };
                return getShadowSubCount(document);
            })();
        ";

        public static string CreateSwitchChatModeScript(bool isAllChat) => $@"
            (async () => {{
                try {{
                    const menuBtn = document.querySelector('div#label-text.style-scope.yt-dropdown-menu, #label-text, yt-dropdown-menu #trigger');
                    if (!menuBtn) return 'BUTTON_NOT_FOUND';

                    menuBtn.click();
                    await new Promise(r => setTimeout(r, 400));

                    const items = Array.from(document.querySelectorAll('div#item-with-badge.style-scope.yt-dropdown-menu, #item-with-badge'));
                    if (items.length === 0) return 'ITEMS_NOT_FOUND';

                    const targetIndex = {(isAllChat ? 1 : 0)};
                    if (items[targetIndex]) {{
                        const targetItem = items[targetIndex];
                        targetItem.click();
                        targetItem.dispatchEvent(new MouseEvent('mousedown', {{ bubbles: true }}));
                        targetItem.dispatchEvent(new MouseEvent('mouseup', {{ bubbles: true }}));
                        return 'SUCCESS';
                    }} else {{
                        return 'INDEX_OUT_OF_RANGE: ' + items.length;
                    }}
                }} catch (e) {{
                    return e.toString();
                }}
            }})();
        ";

        public const string ChatObserver = @"
            (() => {
                if (window.__currentChatObserver) {
                    try {
                        window.__currentChatObserver.disconnect();
                    } catch(e) {}
                }

                const startChatObserver = (retryCount = 0) => {
                    const chatList = document.querySelector('#items.yt-live-chat-item-list-renderer, #item-list #items, yt-live-chat-item-list-renderer #items');
                    if (!chatList) {
                        if (retryCount < 30) {
                            setTimeout(() => startChatObserver(retryCount + 1), 500);
                        }
                        return;
                    }

                    const extractMessageHtml = (messageNode) => {
                        if (!messageNode) return '';
                        let htmlResult = '';
                        messageNode.childNodes.forEach(node => {
                            if (node.nodeType === Node.TEXT_NODE) {
                                htmlResult += node.textContent;
                            } else if (node.nodeType === Node.ELEMENT_NODE) {
                                const tagName = node.tagName.toLowerCase();
                                if (tagName === 'img') {
                                    const src = node.getAttribute('src') || '';
                                    const alt = node.getAttribute('alt') || '';
                                    htmlResult += `<img src=""${src}"" alt=""${alt}"" class=""yt-emoji"" style=""height: 1.2em; vertical-align: middle; margin: 0 2px;"" />`;
                                } else {
                                    htmlResult += extractMessageHtml(node);
                                }
                            }
                        });
                        return htmlResult;
                    };

                    const handleAddedNode = (targetNode) => {
                        if (!targetNode || targetNode.nodeType !== 1) return;

                        // 1. ジュエル判定（yt-gift-message-view-model）
                        const jewelHost = targetNode.matches('yt-gift-message-view-model') 
                            ? targetNode 
                            : targetNode.closest('yt-gift-message-view-model');

                        if (jewelHost) {
                            if (jewelHost.__jewel_handled) return;
                            jewelHost.__jewel_handled = true;

                            const text = (jewelHost.innerText || jewelHost.textContent || '').trim();
                            const countMatch = text.match(/ジュエル\s*(\d[\d,]*)\s*個/i) || text.match(/(\d[\d,]*)\s*(?:個の)?(?:ジュエル|Jewel)/i);
                            const jewelCount = countMatch && countMatch[1] ? parseInt(countMatch[1].replace(/,/g, ''), 10) : 0;

                            const authorMatch = text.match(/^(@[^\s\u00A0]+)/);
                            const author = authorMatch ? authorMatch[1].trim() : (jewelHost.querySelector('#author-name')?.textContent?.trim() || '名無し');

                            // ★ アイコン取得セレクタ強化
                            const avatarImg = jewelHost.querySelector('#author-photo img, yt-img-shadow img, img');
                            const avatarUrl = avatarImg ? (avatarImg.src || avatarImg.currentSrc || avatarImg.getAttribute('src') || '') : '';

                            window.chrome.webview.postMessage(JSON.stringify({
                                type: 'CHAT_MESSAGE',
                                author: author,
                                authorId: '',
                                message: text,
                                avatar: avatarUrl,
                                avatarUrl: avatarUrl,
                                isMember: false,
                                jewels: jewelCount
                            }));
                            return;
                        }

                        // 2. メンバーシップギフト購入コンテナ（gift-purchase に厳密一致）
                        const giftHost = targetNode.matches('ytd-sponsorships-live-chat-gift-purchase-announcement-renderer, yt-live-chat-sponsorships-gift-purchase-announcement-renderer')
                            ? targetNode
                            : targetNode.closest('ytd-sponsorships-live-chat-gift-purchase-announcement-renderer, yt-live-chat-sponsorships-gift-purchase-announcement-renderer');

                        if (giftHost) {
                            if (giftHost.__gift_handled) return;
                            giftHost.__gift_handled = true;

                            const gText = (giftHost.innerText || giftHost.textContent || '').trim();

                            let author = '';
                            const authorMatch = gText.match(/^(@[^\s\u00A0]+)/);
                            if (authorMatch) {
                                author = authorMatch[1].trim();
                            } else {
                                author = giftHost.querySelector('#author-name, .author-name')?.textContent?.trim() || '名無し';
                            }

                            let giftCount = 1;
                            const countMatch = gText.match(/(?:ギフトを?|gifted)\s*(\d[\d,]*)\s*(?:個|人分)/i)
                                            || gText.match(/(\d[\d,]*)\s*(?:個贈りました|人分贈りました)/i)
                                            || gText.match(/(\d[\d,]*)\s*(?:個|人分の)?\s*(?:メンバーシップ\s*ギフト|membership\s*gift)/i);

                            if (countMatch && countMatch[1]) {
                                giftCount = parseInt(countMatch[1].replace(/,/g, ''), 10);
                            }

                            // ★ アイコン取得セレクタ強化
                            const avatarImg = giftHost.querySelector('#author-photo img, yt-img-shadow img, img');
                            const avatarUrl = avatarImg ? (avatarImg.src || avatarImg.currentSrc || avatarImg.getAttribute('src') || '') : '';

                            window.chrome.webview.postMessage(JSON.stringify({
                                type: 'GIFT_EVENT',
                                author: author,
                                authorId: '',
                                avatar: avatarUrl,
                                gifts: giftCount
                            }));
                            return;
                        }

                        // 3. 通常コメント / スパチャ
                        const chatMsgHost = targetNode.matches('yt-live-chat-text-message-renderer, yt-live-chat-paid-message-renderer, yt-live-chat-paid-sticker-renderer')
                            ? targetNode
                            : targetNode.closest('yt-live-chat-text-message-renderer, yt-live-chat-paid-message-renderer, yt-live-chat-paid-sticker-renderer');

                        if (chatMsgHost) {
                            if (chatMsgHost.__chat_handled) return;
                            chatMsgHost.__chat_handled = true;

                            const author = chatMsgHost.querySelector('#author-name')?.textContent?.trim() || '';
                            if (!author) return;

                            const avatarImg = chatMsgHost.querySelector('#author-photo img, yt-img-shadow img, img');
                            const avatarUrl = avatarImg ? (avatarImg.src || avatarImg.currentSrc || avatarImg.getAttribute('src') || '') : '';

                            const authorLink = chatMsgHost.querySelector('a#author-name, #author-photo');
                            const authorId = authorLink ? (authorLink.getAttribute('href') || '') : '';

                            const authorType = chatMsgHost.getAttribute('author-type') || '';
                            const badgeEl = chatMsgHost.querySelector('yt-live-chat-author-badge-renderer');
                            const badgeText = badgeEl ? (badgeEl.getAttribute('aria-label') || badgeEl.innerText || '') : '';
                            const hasMemberBadge = !!chatMsgHost.querySelector('[type=""member""]');
                            const isMember = authorType.toLowerCase().includes('member') || badgeText.includes('メンバー') || hasMemberBadge;

                            const messageNode = chatMsgHost.querySelector('#message');
                            const messageHtml = extractMessageHtml(messageNode);
                            const fullText = (chatMsgHost.innerText || '').trim();

                            window.chrome.webview.postMessage(JSON.stringify({
                                type: 'CHAT_MESSAGE',
                                author: author,
                                authorId: authorId,
                                message: messageHtml || fullText,
                                avatar: avatarUrl,
                                avatarUrl: avatarUrl,
                                isMember: isMember,
                                jewels: 0
                            }));
                        }
                    };

                    window.__currentChatObserver = new MutationObserver((mutations) => {
                        for (const mutation of mutations) {
                            for (const node of mutation.addedNodes) {
                                handleAddedNode(node);
                            }
                        }
                    });

                    window.__currentChatObserver.observe(chatList, { childList: true, subtree: true });
                };

                startChatObserver();
            })();
        ";

        public const string LiveStatsObserver = @"
            (() => {
                const fetchLiveStats = () => {
                    let viewers = '0';
                    let likes = '0';
                    const allCards = Array.from(document.querySelectorAll('ytcp-quick-stat, .metric-container, div'));

                    allCards.forEach(card => {
                        const txt = card.innerText || '';
                        if (txt.includes('同時視聴者数') || txt.includes('Concurrent viewers')) {
                            const valEl = card.querySelector('.value, #value, .metric-value, .value-text');
                            if (valEl) viewers = valEl.innerText.trim();
                        }
                        if (txt.includes('高評価') || txt.includes('Likes')) {
                            const valEl = card.querySelector('.value, #value, .metric-value, .value-text');
                            if (valEl) likes = valEl.innerText.trim();
                        }
                    });

                    window.chrome.webview.postMessage(JSON.stringify({
                        type: 'LIVE_STATS',
                        viewers: viewers.replace(/[^0-9]/g, ''),
                        likes: likes.replace(/[^0-9]/g, '')
                    }));
                };

                fetchLiveStats();
            })();
        ";

        public const string ChatAndReactionObserver = @"
            (() => {
                if (window.__chatAndReactionObserver) {
                    try { window.__chatAndReactionObserver.disconnect(); } catch(e){}
                }

                const checkAndSend = (node) => {
                    if (node.nodeType !== 1) return;
                    const className = typeof node.className === 'string' ? node.className : '';
                    const tagName = node.tagName ? node.tagName.toLowerCase() : '';

                    if (className.includes('yt-emoji-fountain-view-model') || tagName === 'emoji') {
                        const imgEl = node.querySelector('img');
                        const emojiUrl = imgEl ? imgEl.src : '';
                        const emojiText = imgEl ? (imgEl.alt || '💖') : (node.innerText?.trim() || '💖');

                        window.chrome.webview.postMessage(JSON.stringify({
                            type: 'REACTION',
                            emoji: emojiText,
                            imageUrl: emojiUrl
                        }));
                    }
                };

                window.__chatAndReactionObserver = new MutationObserver((mutations) => {
                    for (const mutation of mutations) {
                        for (const node of mutation.addedNodes) {
                            checkAndSend(node);
                            if (node.querySelectorAll) {
                                node.querySelectorAll('.yt-emoji-fountain-view-model, emoji').forEach(checkAndSend);
                            }
                        }
                    }
                });

                window.__chatAndReactionObserver.observe(document.body, { childList: true, subtree: true });
            })();
        ";
    }
}