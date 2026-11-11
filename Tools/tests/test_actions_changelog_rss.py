from __future__ import annotations

import email.utils
import importlib.util
from pathlib import Path
import socket
import unittest
from unittest import mock
import xml.etree.ElementTree as stdlib_ET
from datetime import datetime, timedelta, timezone

SCRIPT = Path(__file__).resolve().parents[1] / "actions_changelog_rss.py"
SPEC = importlib.util.spec_from_file_location("actions_changelog_rss", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
RSS_MOD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RSS_MOD)


class ActionsChangelogRSSTests(unittest.TestCase):
    def setUp(self) -> None:
        # Guard against any network calls during tests
        self._orig_socket_connect = socket.socket.connect

        def _no_network(*args: object, **kwargs: object) -> None:
            raise RuntimeError("Unexpected network access attempted during test execution")

        patcher = mock.patch.object(socket.socket, "connect", side_effect=_no_network)
        self.addCleanup(patcher.stop)
        patcher.start()

    def test_rss_generation_from_synthetic_changelog_entries(self) -> None:
        now = datetime(2026, 11, 5, 12, 0, 0, tzinfo=timezone.utc)
        changelog = {
            "Entries": [
                {
                    "id": 101,
                    "author": "Alice",
                    "time": "2026-11-05T11:00:00.0000000+00:00",
                    "labels": [],
                    "changes": [
                        {"type": "Add", "message": "Added a new widget"},
                        {"type": "Tweak", "message": "Adjusted widget settings"},
                    ],
                },
                {
                    "id": 102,
                    "author": "Bob",
                    "time": "2026-11-05T11:30:00.0000000+00:00",
                    "labels": [RSS_MOD.EXPERIMENTAL_LABEL],
                    "changes": [
                        {"type": "Fix", "message": "Fixed widget crash"},
                        {"type": "Remove", "message": "Removed obsolete widget"},
                    ],
                },
            ]
        }

        rss, any_new = RSS_MOD.create_feed(changelog, [])
        self.assertTrue(any_new)
        self.assertEqual(rss.tag, "rss")
        self.assertEqual(rss.get("version"), "2.0")

        channel = rss.find("./channel")
        self.assertIsNotNone(channel)
        self.assertEqual(channel.find("title").text, RSS_MOD.FEED_TITLE)
        self.assertEqual(channel.find("link").text, RSS_MOD.FEED_LINK)
        self.assertEqual(channel.find("description").text, RSS_MOD.FEED_DESCRIPTION)
        self.assertEqual(channel.find("language").text, RSS_MOD.FEED_LANGUAGE)

        items = channel.findall("./item")
        self.assertEqual(len(items), 1)

        new_item = items[0]
        self.assertEqual(new_item.get(RSS_MOD.XML_NS_B + "from-id"), "0")
        self.assertEqual(new_item.get(RSS_MOD.XML_NS_B + "to-id"), "102")
        self.assertEqual(new_item.find("guid").text, f"{RSS_MOD.FEED_GUID_PREFIX}0-102")

        entries = new_item.findall(f"./{RSS_MOD.XML_NS_B}entry")
        self.assertEqual(len(entries), 2)

        entry_ids = [e.find(f"./{RSS_MOD.XML_NS_B}id").text for e in entries]
        self.assertEqual(entry_ids, ["101", "102"])

        desc = new_item.find("description").text
        self.assertIn("<h3>Alice updated:</h3>", desc)
        self.assertIn("<h3>Bob updated:</h3>", desc)
        self.assertIn("🆕 Added a new widget", desc)
        self.assertIn("⚒️ Adjusted widget settings", desc)
        self.assertIn("🐛🧪 Fixed widget crash", desc)
        self.assertIn("❌🧪 Removed obsolete widget", desc)

    def test_xml_escaping_in_titles_authors_and_messages(self) -> None:
        now = datetime(2026, 11, 5, 12, 0, 0, tzinfo=timezone.utc)
        special_author = "Alice & Bob <special_dev>"
        special_message_1 = "Tweak <foo> & <bar> condition"
        special_message_2 = "Fix if x < 10 && y > 20"

        changelog = {
            "Entries": [
                {
                    "id": 201,
                    "author": special_author,
                    "time": "2026-11-05T11:00:00.0000000+00:00",
                    "labels": [],
                    "changes": [
                        {"type": "Tweak", "message": special_message_1},
                        {"type": "Fix", "message": special_message_2},
                    ],
                }
            ]
        }

        # Also create a synthetic previous item with & and < in title
        prev_item = RSS_MOD.ET.Element(
            "item",
            {
                RSS_MOD.XML_NS_B + "from-id": "100",
                RSS_MOD.XML_NS_B + "to-id": "200",
            },
        )
        special_title = "Update: A & B <v1.0>"
        RSS_MOD.ET.SubElement(prev_item, "title").text = special_title
        RSS_MOD.ET.SubElement(prev_item, "pubDate").text = email.utils.format_datetime(
            now - timedelta(days=1)
        )

        rss, any_new = RSS_MOD.create_feed(changelog, [prev_item])
        self.assertTrue(any_new)

        xml_bytes = RSS_MOD.ET.tostring(rss, encoding="utf-8")
        xml_text = xml_bytes.decode("utf-8")

        # In serialized XML, literal unescaped & and < cannot appear in text nodes
        self.assertIn("Update: A &amp; B &lt;v1.0&gt;", xml_text)
        self.assertIn("Alice &amp; Bob &lt;special_dev&gt;", xml_text)
        self.assertIn("Tweak &lt;foo&gt; &amp; &lt;bar&gt; condition", xml_text)
        self.assertIn("Fix if x &lt; 10 &amp;&amp; y &gt; 20", xml_text)

        # In the HTML description element, HTML entities must be escaped inside XML
        self.assertIn("&amp;amp;", xml_text)
        self.assertIn("&amp;lt;", xml_text)

    def test_newest_first_ordering(self) -> None:
        now = datetime(2026, 11, 5, 12, 0, 0, tzinfo=timezone.utc)
        time_old = now - timedelta(days=1)
        time_older = now - timedelta(days=2)
        time_oldest = now - timedelta(days=3)

        def make_prev_item(from_id: int, to_id: int, pub_date: datetime) -> RSS_MOD.ET.Element:
            item = RSS_MOD.ET.Element(
                "item",
                {
                    RSS_MOD.XML_NS_B + "from-id": str(from_id),
                    RSS_MOD.XML_NS_B + "to-id": str(to_id),
                },
            )
            RSS_MOD.ET.SubElement(item, "title").text = f"Item {from_id}-{to_id}"
            RSS_MOD.ET.SubElement(item, "pubDate").text = email.utils.format_datetime(pub_date)
            return item

        prev_items = [
            make_prev_item(200, 300, time_old),
            make_prev_item(100, 200, time_older),
            make_prev_item(0, 100, time_oldest),
        ]

        changelog = {
            "Entries": [
                {
                    "id": 301,
                    "author": "Alice",
                    "time": "2026-11-05T11:00:00.0000000+00:00",
                    "labels": [],
                    "changes": [{"type": "Add", "message": "Brand new change"}],
                }
            ]
        }

        rss, any_new = RSS_MOD.create_feed(changelog, prev_items)
        self.assertTrue(any_new)

        items = rss.findall("./channel/item")
        self.assertEqual(len(items), 4)

        # Top item must be the newest created item (from-id: 300, to-id: 301)
        item_to_ids = [int(i.get(RSS_MOD.XML_NS_B + "to-id")) for i in items]
        self.assertEqual(item_to_ids, [301, 300, 200, 100])

        item_from_ids = [int(i.get(RSS_MOD.XML_NS_B + "from-id")) for i in items]
        self.assertEqual(item_from_ids, [300, 200, 100, 0])

    def test_output_parses_as_valid_xml(self) -> None:
        now = datetime(2026, 11, 5, 12, 0, 0, tzinfo=timezone.utc)
        changelog = {
            "Entries": [
                {
                    "id": 1,
                    "author": "Dev & Contributor <dev@example.com>",
                    "time": "2026-11-05T10:00:00.0000000+00:00",
                    "labels": ["Intent: Experimental"],
                    "changes": [
                        {"type": "Fix", "message": "Fixed <xml> entity & escaping"},
                        {"type": "Add", "message": "Added \"quoted\" & 'single' test"},
                    ],
                }
            ]
        }

        rss, any_new = RSS_MOD.create_feed(changelog, [])
        self.assertTrue(any_new)

        xml_bytes = RSS_MOD.ET.tostring(
            rss,
            encoding="utf-8",
            xml_declaration=True,
            doctype="<?xml-stylesheet type='text/xsl' href='./stylesheet.xsl'?>",
        )

        # 1. Parse with lxml
        parsed_lxml = RSS_MOD.ET.fromstring(xml_bytes)
        self.assertEqual(parsed_lxml.tag, "rss")

        # 2. Parse with stdlib ElementTree
        parsed_stdlib = stdlib_ET.fromstring(xml_bytes)
        self.assertEqual(parsed_stdlib.tag, "rss")

        # Verify element contents parsed correctly back to Python strings without corruption
        entry_elem = parsed_stdlib.find(f".//{{{RSS_MOD.XML_NS}}}entry")
        self.assertIsNotNone(entry_elem)
        author_elem = entry_elem.find(f"{{{RSS_MOD.XML_NS}}}author")
        self.assertEqual(author_elem.text, "Dev & Contributor <dev@example.com>")

        changes = entry_elem.findall(f"{{{RSS_MOD.XML_NS}}}change")
        self.assertEqual(len(changes), 2)
        self.assertEqual(changes[0].text, "Fixed <xml> entity & escaping")
        self.assertEqual(changes[1].text, "Added \"quoted\" & 'single' test")

    def test_expired_previous_items_are_pruned(self) -> None:
        now = datetime(2026, 11, 5, 12, 0, 0, tzinfo=timezone.utc)
        expired_date = now - timedelta(days=35)
        recent_date = now - timedelta(days=5)

        def make_prev_item(from_id: int, to_id: int, pub_date: datetime) -> RSS_MOD.ET.Element:
            item = RSS_MOD.ET.Element(
                "item",
                {
                    RSS_MOD.XML_NS_B + "from-id": str(from_id),
                    RSS_MOD.XML_NS_B + "to-id": str(to_id),
                },
            )
            RSS_MOD.ET.SubElement(item, "pubDate").text = email.utils.format_datetime(pub_date)
            return item

        prev_items = [
            make_prev_item(50, 100, recent_date),
            make_prev_item(0, 50, expired_date),
        ]

        channel = RSS_MOD.ET.Element("channel")
        RSS_MOD.copy_previous_items(channel, prev_items, now)

        items = channel.findall("./item")
        self.assertEqual(len(items), 1)
        self.assertEqual(items[0].get(RSS_MOD.XML_NS_B + "to-id"), "100")

    def test_no_new_entries_returns_false(self) -> None:
        changelog = {
            "Entries": [
                {
                    "id": 50,
                    "author": "Alice",
                    "time": "2026-11-01T10:00:00.0000000+00:00",
                    "labels": [],
                    "changes": [{"type": "Fix", "message": "Old fix"}],
                }
            ]
        }
        prev_item = RSS_MOD.ET.Element(
            "item",
            {
                RSS_MOD.XML_NS_B + "from-id": "0",
                RSS_MOD.XML_NS_B + "to-id": "50",
            },
        )
        RSS_MOD.ET.SubElement(prev_item, "pubDate").text = email.utils.format_datetime(
            datetime.now(timezone.utc)
        )

        rss, any_new = RSS_MOD.create_feed(changelog, [prev_item])
        self.assertFalse(any_new)
        self.assertEqual(len(rss.findall("./channel/item")), 1)


if __name__ == "__main__":
    unittest.main()
