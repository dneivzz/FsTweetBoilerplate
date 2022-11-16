$(function () {
  $("#tweetForm").submit(function (event) {
    event.preventDefault();
    $.ajax({
      url: "/tweets",
      type: "post",
      data: JSON.stringify({ post: $("#tweet").val() }),
      contentType: "application/json"
    }).done(function () {
      alert("successfully posted")
    }).fail(function (jqXHR, textStatus, errorThrown) {
      console.log({
        jqXHR: jqXHR,
        textStatus: textStatus,
        errorThrown: errorThrown
      })
      alert("something went wrong!")
    });
  });

  let client =
    stream.connect(fsTweet.stream.apiKey, null, fsTweet.stream.appId);

  let userFeed =
    client.feed("user", fsTweet.user.id, fsTweet.user.feedToken);
  userFeed.subscribe(function (data) {
    renderTweet($("#wall"), data.new[0]);
  });
  let timelineFeed =
    client.feed("timeline", fsTweet.user.id, fsTweet.user.feedToken);
  timelineFeed.subscribe(function (data) {
    renderTweet($("#wall"), data.new[0]);
  });
  timelineFeed.get({
    limit: 25
  }).then(function (body) {
    var timelineTweets = body.results
    userFeed.get({
      limit: 25
    }).then(function (body) {
      var userTweets = body.results
      var allTweets = $.merge(timelineTweets, userTweets)
      allTweets.sort(function (t1, t2) {
        return new Date(t2.time) - new Date(t1.time);
      })
      $(allTweets.reverse()).each(function (index, tweet) {
        renderTweet($("#wall"), tweet);
      });
    })
  })
});