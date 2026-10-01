const { onDocumentCreated } = require("firebase-functions/v2/firestore");
const { initializeApp } = require("firebase-admin/app");
const { getMessaging } = require("firebase-admin/messaging");
const { getFirestore } = require("firebase-admin/firestore");

initializeApp();

exports.onHighPriorityAlert = onDocumentCreated(
  "admin_alerts/{alertId}",
  async (event) => {
    const data = event.data?.data();
    if (!data) return;

    // Only fire for High Priority
    if (data.priority !== "High") return;

    try {
      const db = getFirestore();
      const adminSnap = await db
        .collection("users")
        .where("role", "==", "admin")
        .get();

      if (adminSnap.empty) {
        console.log("No admin users found.");
        return;
      }

      const messages = [];
      adminSnap.forEach((doc) => {
        const fcmToken = doc.data().fcmToken;
        if (!fcmToken) return;

        messages.push({
          token: fcmToken,
          notification: {
            title: "🚨 HIGH PRIORITY ALERT",
            body: `Emergency at ${data.location} — immediate response needed!`,
          },
          android: {
            priority: "high",
            notification: {
              channelId: "high_priority_channel",
              sound: "default",
              defaultVibrateTimings: true,
            },
          },
          data: {
            type: "HIGH_PRIORITY_ALERT",
            priority: "High",
            location: data.location ?? "",
          },
        });
      });

      if (messages.length === 0) {
        console.log("No admin FCM tokens found.");
        return;
      }

      const response = await getMessaging().sendEach(messages);
      console.log(`Sent ${response.successCount} notifications.`);

    } catch (err) {
      console.error("Error sending notification:", err);
    }
  }
);